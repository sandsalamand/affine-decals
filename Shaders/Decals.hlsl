#ifndef URP_EXAMPLE_SURFACE_SHADER_INCLUDED
#define URP_EXAMPLE_SURFACE_SHADER_INCLUDED

#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR

/////////////////////////////////////////
// Include this at the start of your file
/////////////////////////////////////////

#include "DecalStructs.hlsl"

////////////////////////////////////////////
// Use this define to modify an input vertex 
////////////////////////////////////////////

// #define UPDATE_INPUT_VERTEX ModifyInputVertex

// inline void ModifyInputVertex(inout Attributes i)
// {
// }

#if defined(FORWARD_PASS) || defined(GBUFFER_PASS)

#define UPDATE_ATTRIBUTES_TO_VARYINGS ModifyAttributesToVaryings

inline void ModifyAttributesToVaryings(in Attributes input, inout GeometryOut output)
{
	//Obsolete approach:	multiply w so it can be used later to de-perspective the UV
	//output.uv = input.texcoord * vertexInput.positionCS.w;	//multiply w so it can be used later to de-perspective the UV

	output.uv = input.texcoord;
}

#endif

/////////////////////////////////////////////
// Use this to change the surfaces properties
/////////////////////////////////////////////

#if defined(FORWARD_PASS) || defined(GBUFFER_PASS)

#include "DecalLitInput.hlsl"

#define GET_SURFACE_PROPERTIES GetSurfaceProperties

#define UNITY_PI            3.14159265359f

TrianglePoint TriPointFromVarying(Varyings varyings)
{
	TrianglePoint trianglePoint;
	trianglePoint.uv = varyings.uv;
	trianglePoint.positionWS = varyings.positionWS;
	trianglePoint.normalWS = varyings.normalWS;
	return trianglePoint;
}

#if defined(DRAW_BACKFACE)
[maxvertexcount(6)]
#else
[maxvertexcount(3)]
#endif
void Geometry(
    triangle Varyings input[3], uint pid : SV_PrimitiveID,
    inout TriangleStream<GeometryOut> triStream
)
{
	for (int i = 0; i < 3; i++)
	{
		GeometryOut geoOut = VertexToGeometryStandard(input[i]);

		//Each GeoOut needs its own copy of the three triangles, so that the Fragment stage can access them
		geoOut.trianglePoint1 = TriPointFromVarying(input[0]);
		geoOut.trianglePoint2 = TriPointFromVarying(input[1]);
		geoOut.trianglePoint3 = TriPointFromVarying(input[2]);

		//Extrude in direction of normals
		// float ext = saturate(0.4 - cos(_Time * UNITY_PI * 2) * 0.41);
		// ext *= 1 + 0.3 * sin(pid * 832.37843 + _Time * 88.76);

		//geoOut.positionWS += (i * 0.1 * geoOut.normalWS);
		//geoOut.positionCS = TransformWorldToHClip(geoOut.positionWS);

		triStream.Append(geoOut);
	}
	triStream.RestartStrip();

#if defined(DRAW_BACKFACE)
	//Duplicate each triangle while reversing the order of vertices and inverting normals
	for(i = 2; i >= 0; i--)
    {
		GeometryOut geoOut = VertexToGeometryStandard(input[i]);
        geoOut.normalWS *= -1;

		geoOut.trianglePoint1 = TriPointFromVarying(input[0]);
		geoOut.trianglePoint2 = TriPointFromVarying(input[1]);
		geoOut.trianglePoint3 = TriPointFromVarying(input[2]);

		triStream.Append(geoOut);
    }
	triStream.RestartStrip();
#endif
}

float ScalarCross2D(float2 v1, float2 v2)
{
	return (v1.x*v2.y) - (v2.x*v1.y);
}

//Find barycentric coordinates of point P between 2D points A, B, and C, and then interpolate between the corresponding float3 data
float3 InterpolateBetween2DCoordsBarycentric(float2 P, float2 A, float2 B, float2 C, float3 aData, float3 bData, float3 cData)
{
	float parallelogramArea = abs(ScalarCross2D(B - A, C - A));

	float a = abs(ScalarCross2D(P - B, C - B)) / parallelogramArea;
	float b = abs(ScalarCross2D(P - C, A - C)) / parallelogramArea;
	float c = abs(ScalarCross2D(P - A, B - A)) / parallelogramArea;

	return  (aData * a) + (bData * b) + (cData * c);
}

float3 UnpackNormalmapRGorAG(float4 packednormal)
{
    // This do the trick
    packednormal.x *= packednormal.w;
    float3 normal;
    normal.xy = packednormal.xy * 2 - 1;
    normal.z = sqrt(1 - saturate(dot(normal.xy, normal.xy)));
    return normal;
}

//TODO: make this accept GeometryOut at its definition
inline SurfaceData GetSurfaceProperties(GeometryOut input)
{
	SurfaceData outSurfaceData;
	
	float3 affineInterpolatedPositionWS = input.positionWS;
	float3 affineInterpolatedNormalWS = input.normalWS;

	//World position affine shift:
	float2 P = input.uv;

	float2 A = input.trianglePoint1.uv;
	float2 B = input.trianglePoint2.uv;
	float2 C = input.trianglePoint3.uv;

	affineInterpolatedPositionWS = InterpolateBetween2DCoordsBarycentric(P, A, B, C,
		input.trianglePoint1.positionWS, input.trianglePoint2.positionWS, input.trianglePoint3.positionWS);

	//This doesn't seem to help at all for curved surfaces. Investigate or remove to save 3 semantics
	affineInterpolatedNormalWS = InterpolateBetween2DCoordsBarycentric(P, A, B, C,
		input.trianglePoint1.normalWS, input.trianglePoint2.normalWS, input.trianglePoint3.normalWS);

	// if (a <= 1 && b <= 1 && c <= 1 && a + b + c == 1)
	// {
	// 	input.uv = float2(0,0);
	// }


	//UV Pixelation
	float2 uv = floor(input.uv.xy * _TextureResolution) / _TextureResolution;
			
	/////////////////////////////////////////////////////////////////////////////////////////
	// Default PBR surface properties match InitializeStandardLitSurfaceData in LitInput.hlsl
	/////////////////////////////////////////////////////////////////////////////////////////

    outSurfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
	
	half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));

	//===== For debugging. Remove later ======
	if (input.uv.x > 2 || input.uv.y > 2 )
	{
		albedoAlpha.rgb = float3(1,0,0);
	}
	else if (input.uv.x < 0 || input.uv.y < 0)
	{
		albedoAlpha.rgb = float3(0,0,1);
	}
	//=========================================

	[unroll]
	for (int i = 0; i < MAX_DECALS; i++)
	{
		float3 positionWS = affineInterpolatedPositionWS;

		float3 decalPoint = _BulletDecalPositions[i].xyz;

		//We use the w channels on these float4s to store decal width and height
		float2 decalTexSize = float2(_BulletDecalNormals[i].w, _BulletDecalTangents[i].w);

		//This defines the ratio between tex size and the width of the texture array slice. Each decal takes up an entire slice, even if it's not the max width.
		//This could at some point be improved with an atlasing scheme, but it would need to be calculated on the C# end.
		float2 decalSizeRatioOnTexArraySlice = (decalTexSize / DECAL_TEX_MAX_WIDTH);

		//This assume x and y are the same of the decal, which is not guaranteed to be true.
		//To fix this, we need to project first into 2D, and then check the x and y to see how far away they are
		if (length(decalPoint - positionWS) > decalSizeRatioOnTexArraySlice.x) //|| dot(_BulletDecalNormals[i].xyz, input.normalWS) <= 0)
			continue;

		
		//Project bullet point onto plane (in case the collider is not exactly in-line with the mesh)
		// float3 v = decalPoint - positionWS;
		// float3 n = input.normalWS;
		// float dist = dot(v, n);
		// float3 projected_point = decalPoint - (dist * n);

		//Project world pos onto plane that bullet hit
		float3 v = positionWS - decalPoint;
		float3 decalNormal = _BulletDecalNormals[i].xyz;
		float dist = dot(v, decalNormal);
		float3 projectedPoint = positionWS - (dist * decalNormal);

		//PROBLEM: the normalized vector scales in all axes, so a projectedPoint of (4,1) ends up as a wrappedPoint of (8.85, 2.22)

		//Updated idea: take the vector of projectedPoint - positionWS, rotate it to match the direction of the tangentWS, somehow consider negatives for left side of cube, and then add that vector to projectedPoint

		//normalize it in relation to the decal origin
		float3 projectedNormalizedPoint = normalize(projectedPoint - decalPoint);

		//Then calculate distance between the projected point and the original positionWS
		float projectionDistance = length(projectedPoint - positionWS);

		//Add the distance between to get a point which is wrapped outward along the plane.
		//Ex: For a cube, the decalPoint is on the front face close to an edge. Take a point on a different side face
		//If this point is close to the front face, the projectionDistance will be lower, so wrappedPoint will be close enough to draw from the 3D bullet positions
		//float3 wrappedPointOnPlane = (projectedNormalizedPoint * projectionDistance) + projectedPoint;

		float3 wrappedPointOnPlane = projectedPoint + (input.normalWS * projectionDistance);
		
		//wrappedPointOnPlane += decalPoint;

		//Replaced positionWS with wrappedPointOnPlane
		float3 r_P = wrappedPointOnPlane;
		float3 r_O = decalPoint;

		//get the two orthonormal plane vectors;
		float3 e_1 = _BulletDecalTangents[i].xyz;	//TODO: instead of this, use the tangent of the decal point plane
		float3 e_2 = cross(e_1, decalNormal);

		//Take dot products to see where the vector (from origin to point) lies between e_1 and e_2.
		//If the dot is less than 0, then the difference between e_1 and the vector is greater than 90 degrees, so it's outside of the UV plane. 
		//If the dot is greater than 1, then the vector is within 90 degrees, but the vec is too long to be inside the UV 0-1 plane
		float t_1 = dot(e_1, r_P - r_O);
		float t_2 = dot(e_2, r_P - r_O);

		//Dividing scales the UV coordinates up
		t_1 = t_1 / decalSizeRatioOnTexArraySlice.x;
		t_2 = t_2 / decalSizeRatioOnTexArraySlice.y;

		//t_1 and t_2 can be negative when r_P is less than r_0 (e.x. left of or below the bullet point)
		t_1 += 0.5; 
		t_2 += 0.5;

		float2 decalUV = float2(t_1, t_2);

		//Since smaller decals are packed into large slices, we need to apply the ratio to the UV
		decalUV = decalUV * decalSizeRatioOnTexArraySlice;

		//Consider if we want to pixellate before or after scaling the texture

		//UV Pixelation
		decalUV = floor(decalUV.xy * _TextureResolution) / _TextureResolution;
		
		//																											w stores the corresponding decal index
		float4 decalAlbedo = SAMPLE_TEXTURE2D_ARRAY(_DecalAlbedoArray, sampler_point_clamp_DecalAlbedoArray, decalUV, _BulletDecalPositions[i].w);
		albedoAlpha.rgb = lerp(albedoAlpha.rgb, decalAlbedo.rgb, decalAlbedo.a);

		// float4 decalAlbedo = SAMPLE_TEXTURE2D(_DecalAlbedo, sampler_point_clamp_DecalAlbedo, decalUV);
		// albedoAlpha.rgb = lerp(albedoAlpha.rgb, decalAlbedo.rgb, decalAlbedo.a);

		//Consideration: might want to use _DecalNormal_TexelSize to divide the original t_1 if normal map size can differ from albedo map
		//float4 decalSampledNormal = SAMPLE_TEXTURE2D(_DecalNormal, sampler_point_clamp_DecalNormal, decalUV);
		//outSurfaceData.normalTS = lerp(outSurfaceData.normalTS.rgb, decalSampledNormal.rgb, decalSampledNormal.a);
	}

	//Color Pixelation
	albedoAlpha = half4((floor(albedoAlpha.rgb * _ColorPrecision) / _ColorPrecision), albedoAlpha.a);

    outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

    half4 specGloss = SampleMetallicSpecGloss(uv, albedoAlpha.a);
    outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;

#if _SPECULAR_SETUP
    outSurfaceData.metallic = 1.0h;
    outSurfaceData.specular = specGloss.rgb;
#else
    outSurfaceData.metallic = specGloss.r;
    outSurfaceData.specular = half3(0.0h, 0.0h, 0.0h);
#endif

    outSurfaceData.smoothness = specGloss.a;
    outSurfaceData.occlusion = SampleOcclusion(uv);
    outSurfaceData.emission = SampleEmission(uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));

#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half2 clearCoat = SampleClearCoat(uv);
    outSurfaceData.clearCoatMask       = clearCoat.r;
    outSurfaceData.clearCoatSmoothness = clearCoat.g;
#else
    outSurfaceData.clearCoatMask       = 0.0h;
    outSurfaceData.clearCoatSmoothness = 0.0h;
#endif

#if defined(_DETAIL)
    half detailMask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv).a;
    float2 detailUv = uv * _DetailAlbedoMap_ST.xy + _DetailAlbedoMap_ST.zw;
    outSurfaceData.albedo = ApplyDetailAlbedo(detailUv, outSurfaceData.albedo, detailMask);
    outSurfaceData.normalTS = ApplyDetailNormal(detailUv, outSurfaceData.normalTS, detailMask);

#endif

	return outSurfaceData;
}

#endif

/////////////////////////////////////////////////////////
// Use this to change the surfaces properties for shadows
/////////////////////////////////////////////////////////

#if defined(SHADOWS_PASS)

#define UPDATE_SHADOW_SURFACE UpdateShadowSurfaceProperties

inline void UpdateShadowSurfaceProperties(Varyings input)
{
	//Update input to alter the shadows if needed
}

#endif

#endif // URP_EXAMPLE_SURFACE_SHADER_INCLUDED