This repository provides the code necessary to create affine-mapped decals in Unity’s Universal Render Pipeline.

If you want to skip to the technical overview, you can click [here](#technical-overview).

## What is affine texture mapping?
As illustrated in Daniel Ilett’s [blog](https://danielilett.com/2021-11-06-tut5-21-ps1-affine-textures/), affine mapping refers to perspective texture mapping which fails to take into account the z-axis. Here is an example of what it looks like on a textured quad which is leaning away from the camera.

![part21-affine-mapping](https://github.com/user-attachments/assets/299db424-ad7b-48ba-857e-43ff74c88118)

The ideal mapping which considers the z-axis looks like this:

![part21-perspective-correct](https://github.com/user-attachments/assets/d35cb74a-e2c5-4126-ae9a-43e9896558ce)

In the first picture, the two triangles are mapped differently. This is because, during the vertex stage of the graphics pipeline, the z-axis is not taken into account when interpolating between vertices of the triangle.

## PS1

The PS1 hardware graphics API had this issue of affine mapping, which is why many games on the PS1 had texture distortion. Some games attempted to solve this problem using tesselation, which is a technique to automatically subdivide polygons based on some criteria (usually distance to the player camera).
By dividing triangles repeatedly, you could eventually get to a point where the effect from z-axis interpolation was so low that it became unnoticable.

However, tesselation was complicated and difficult to implement, so most games resorted to simply adding extra redundant vertices to reduce the noticeability of affine warping.
As a result, this was a typical look from a PS1 game (Metal Gear Solid).

![image](https://github.com/user-attachments/assets/61e5ebf9-d085-460d-9676-38f14b42f0d1)

If you look carefully at the walls and floors, you can see that the textures are wavy. Part of this is due to [vertex jitter](https://retrocomputing.stackexchange.com/questions/5019/why-do-3d-models-on-the-playstation-1-wobble-so-much), but affine warping is mostly to blame.

## Our game

For our survival horror game, our lead artist wanted to emulate some visual characteristics from PS1 and PS2 games. This style of cherrypicking attributes from the PS1 and PS2 is known as "PSX". In Unity, affine mapping is easily achieved by using the `noperspective` keyword in HLSL. Here is what it looks like on some of our floor and wall textures.

![image](https://github.com/user-attachments/assets/32280056-60f4-48fe-a5e2-69039ed41aa4)

Since our game involves shooting zombies, we determined that decals were a high priority for blood spatters and bullet holes.

## The problem:
Unity’s built-in decal system does what a modern engine should do, and takes into account the z-axis. 

After lots of fiddling to try to create a custom decal shader which was compatible with Unity's URP Decal Projector and also produced an effect that mimicked affine warping, I realised that this was impossible.
The bounds of the texture on the screen are strictly defined by the orthographic projection matrix of the decal projector, but in order for the decal to match the warping of the wall texture that it's on top of, the decal must be able to stretch beyond that space. Here is a picture illustrating the problem:

![image](https://github.com/user-attachments/assets/7e126371-913c-4af0-9b4f-e70493ad658f)

If you look carefully, you can see that the warning sign decals are not distorting in accordance with the cube texture behind it. 

## The solution:
I dug deep into HLSL and Unity's Universal Render Pipeline in order to create a custom decal system which takes affine mapping into account. This is what the new system looks like with the same decal locations.

![image](https://github.com/user-attachments/assets/b66e97f6-8795-4191-a7e9-cb735c0516ec)

This has the nice side effect of allowing us to match the pixel density of the decal texture to the density of the texture behind it.

## Technical Overview

Each static object in the scene has the PSX_DecalV2 shader. For each fragment, the shader checks all of the entries of the decal position array `_BulletDecalPositions` and compares the distance to determine if the fragment should draw any decals.
If so, then `_BulletDecalPositions.w` is used as the index to a Texture2DArray to determine which texture to draw.

### Affine shifting

My first thought was to use the `noperspective` trick to get affine-warped UVs and use those to sample from the decal texture. However, the problem is that the UVs of the wall/object in the scene are not the same as the UVs which are needed to properly sample the decal texture.
For example, if we start drawing a decal near the bottom left corner of a square object in the scene, the UVs in the fragment stage might be (0.2, 0.2). But the decal texture needs to be sampled from the absolute bottom left of the decal, at (0, 0). We also need to draw the entirety of the decal texture. So when the UVs for the square are (0.3, 0.3), we might need to be drawing (1, 1) for the top right corner of a small decal.

One approach which seemed promising was drawing a certain offset away from the decal center position, based upon the size of the decal texture. However, this and similar solutions do not consider the affine shift of the underlying object texture.

After many failed ideas, and even an attempt at using triplanar mapping, I finally realized that the only way to make this work was by passing the raw vertex world positions from the geometry stage, before they are interpolated in the vertex stage.
This allows us to sidestep the perspective-correct interpolation and directly compute the fragment world position based upon the affine-shifted barycentric coordinates of the 3 vertices which make up the triangle.
With this affine-shifted world position, we can do some linear algebra to make sure the decal wraps around edges. Finally, I can calculate the affine-shifted UV to sample the decal texture at the correct location.

### Sampling

After all of the UV shifting is done, we sample from a Texture2DArray using the index of the correct decal texture. We then lerp with `albedoAlpha`, which is the base texture of the wall/object that the decal is being painted onto.
This lerp allows the decal to blend appropriately with the underlying texture based upon the transparency of the decal.
```
float4 decalAlbedo = SAMPLE_TEXTURE2D_ARRAY(_DecalAlbedoArray, sampler_point_clamp_DecalAlbedoArray, decalUV, _BulletDecalPositions[i].w);
albedoAlpha.rgb = lerp(albedoAlpha.rgb, decalAlbedo.rgb, decalAlbedo.a);
```

### CPU->GPU Data Flow

When a new decal needs to be added to the scene, the C# code passes 3 updated arrays of Vector4s to the GPU with `Shader.SetGlobalVectorArray`. Since Unity doesn't keep track of the contents of our GPU arrays, we need to keep a copy of the 3 arrays on the CPU and update them accordingly.
A FixedQueue is used to manage these buffers because the GPU buffers are fixed, and can only be changed by recompiling with a greater constant. If a script attempts to spawn a decal when the max number has already been reached, then the oldest decal is dequeued and the newest one is enqueued. This change is reflected in the scene with the oldest decal disappearing when the GPU buffers are updated.



## Challenges:

### HLSL dependencies
In order to make shaders that consider lighting and shadows in Unity URP, there is a lot of boilerplate that you need to include in your .shader files. Hand-written shaders need to #include a bunch of HLSL files, and #pragma a ton of keywords. This lack of ergonomics is due to Unity's shift to a node-based, graphical shader editor, which unfortunately still lacks many features. Thankfully, I was able to use this [surface shader base](https://github.com/traggett/UnityUniversalRPSurfaceShader) made by Traggett to avoid having to research all of the inner workings of URP lighting and shadows. However, due to my use of the geometry shader stage, I was forced to heavily modify most of the files from the template.

### Upgrading from Texture2D to Texture2DArray
Throughout early development, I sampled from a single Texture2D which was assigned in an Inspector reference. For a real game, the system needed to be expanded to work for any arbitrary number of textures created by artists and assigned in the Editor. Rather than sampling from a Texture2D in the fragment stage, we need to use a Texture2DArray which stores all of the possible decal textures for the level.
Each decal position's corresponding texture index is stored in the w coordinate of the Vector4 in `_BulletDecalPositions`. We also store the width and height of each texture in the w coordinates of `_BulletDecalNormals` and `_BulletDecalTangents`.
While it would be nice to properly organize this into a structure, Unity only allows us to send lists of data to the GPU in the form of Vector4 or other basic structures.
    
### Serialization 
Texture2DArray cannot be serialized by the built-in Unity serializer. This means that we cannot take advantage of Unity’s built-in logic for when to serialize and deserialize objects within the Editor. 
The standard solution is to use ISerializationCallbackReceiver, but due to the fact that the Texture2DArray data changes only on the GPU, Unity often fails to call OnBeforeSerialize properly.
This creates a lot of challenges regarding switching scenes and opening/closing the Editor, since one missed serialize/deserialize can result in lost or corrupted data. While I have created the basic functionality and manually tested basic scenarios, there are probably many more uncaught edge cases.


## To-do:
- The code in this repository was simply copied and pasted from our game's source files. In the future, it should be separated as a package with a manifest.json.
- Editor tooling needs a lot of work.
- Serialization is barebones and needs to be thoroughly tested.
- It might be possible to use tesselation stage instead of the geometry stage to pass raw vertex data to the fragment stage, so that the shader is compatible with the Metal Graphics API.
- This code was designed to work with static scene objects. Moving objects could technically work by refreshing the decal positions every `FixedUpdate` with `Shader.SetGlobalVectorArray`, but this might prove to be prohibitively slow.
  - Idea one: Have all moving objects write to one central list, and push all of these changes to the GPU simultaneously in one `SetGlobalVectorArray` call in FixedUpdate. This would allow you to have many moving objects with only one push per FixedUpdate.
  - Idea two: Have each moving object keep its own individual list of decals that should be drawn upon it. Use a compute shader to determine where the new decal position should be drawn each frame. This allows you to do GPU->GPU data transfer from the compute shader to the decal shader, which is likely more performant.


## Optimization Opportunities:
- Small textures still take up an entire slice of the Texture2DArray. Since each slice is fixed to be as large as the largest decal texture in the game, this can result in some wasted VRAM. This could be solved with some complicated indexing strategy where multiple small textures can share one slice.
- There are probably more efficient ways to apply the necessary transformations to the affine-shifted world positions. I used several dot and cross products in the main loop, but it's probably possible to cache some matrices to speed up the calculations for unmoving decals on static objects.
