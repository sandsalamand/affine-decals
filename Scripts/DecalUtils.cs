using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public static class DecalUtils
{
	const string decalShaderName = "Custom/PSX_DecalV2";

	public static void SpawnStandardURPDecal(GameObject decalToSpawn, GameObject parent, Vector3 position, Vector3 facingDirection)
	{
		GameObject decalProjector = GameObject.Instantiate(decalToSpawn, position, Quaternion.LookRotation(facingDirection));
		//decalProjector.transform.Rotate(facingDirection, Space.World);
		decalProjector.transform.SetParent(parent.transform);
	}

	public static void DrawDecalOnSkinnedMeshRenderer(SkinnedMeshRenderer skinnedMeshRenderer, Vector3 hitOrigin, Texture2D albedoMap, Texture2D normalMap)
	{
		var bakedSkinMesh = new Mesh();
		skinnedMeshRenderer.BakeMesh(bakedSkinMesh);

		//TODO: make this actually work
		Vector2 nearestUV = FindNearestUV(bakedSkinMesh, hitOrigin);
		//Debug.Log("Nearest UV: " + nearestUV);

		Material material = skinnedMeshRenderer.material;

		DrawTextureOnMaterial(material, nearestUV.x, nearestUV.y, albedoMap, normalMap);
	}

	/// <summary>
	/// Use this version if you don't know the UV coordinates, but you have a world position which is close to the material
	/// </summary>
	public static void DrawDecalOnHitEnvironment(GameObject hitGameObject, Vector3 nearestWorldPos, Texture2D albedoMap, Texture2D normalMap, GameObject debugPrefab)
	{
		MeshFilter meshFilter = hitGameObject.GetComponent<MeshFilter>();
		if (meshFilter == null)
		{
			Debug.Log("Nearest object did not contain a MeshFilter. Cannot draw decal on it");
			return;
		}
		var result = BaryCentricDistance.GetClosestTriangleAndPoint(nearestWorldPos, hitGameObject.transform, meshFilter.sharedMesh);
		//result.
		//var debugGameObj = GameObject.Instantiate(debugPrefab);
		//debugGameObj.transform.position = result.closestPoint + (result.normal * 0.1f);


		MeshRenderer meshRenderer = hitGameObject.GetComponent<MeshRenderer>();
		if (meshRenderer == null)
		{
			Debug.Log("Nearest object did not contain a MeshRenderer. Cannot draw decal on it");
			return;
		}

		//Multiply by each UV corresponding to the 3 vertices of the triangle
		Vector2 uv = new Vector2(result.triangleUVs[0].x + result.triangleUVs[1].x + result.triangleUVs[2].x, result.triangleUVs[0].y + result.triangleUVs[1].y + result.triangleUVs[2].y);

		//Vector2 uv = FindNearestUV(meshFilter.sharedMesh, nearestWorldPos);
		Debug.Log("UV is " + uv);
		DrawTextureOnMaterial(meshRenderer.material, uv.x, uv.y, albedoMap, normalMap);
	}

	public static void DrawDecalOnHitEnvironment(GameObject hitGameObject, float uvX, float uvY, Texture2D albedoMap, Texture2D normalMap)
	{
		if (TryGetMaterialAndRenderer(hitGameObject, out Material material, out Renderer renderer))
			DrawTextureOnMaterial(material, uvX, uvY, albedoMap, normalMap);
	}

	private static void DrawTextureOnMaterial(Material material, float uvX, float uvY, Texture2D albedoMap, Texture2D normalMap)
	{
		if (material.shader.name != decalShaderName)
			return;

		const string normalMapPropertyName = "_BumpMap";
		const string albedoMapPropertyName = "_BaseMap";

		//Have to check these because some shader variants don't include the properties
		if (material.HasTexture(normalMapPropertyName))
		{
			OverwriteTextureAtUVPos(material, normalMapPropertyName, uvX, uvY, normalMap, blendFunction: BlendNormalMaps);
		}
		if (material.HasTexture(albedoMapPropertyName))
		{
			OverwriteTextureAtUVPos(material, albedoMapPropertyName, uvX, uvY, albedoMap, blendFunction: BlendAlbedoColor);
		}
	}

	private static void OverwriteTextureAtUVPos(Material material, string textureMaterialPropertyName, float uvX, float uvY, Texture2D textureToDraw, Func<Color, Color, Color> blendFunction)
	{
		Texture2D baseMap = (Texture2D)material.GetTexture(textureMaterialPropertyName);

		if (baseMap == null)
		{
			Debug.LogWarning($"Material {material.name} is missing the property {textureMaterialPropertyName}. This part of the projectile decal will not function");
			return;
		}

		//Scale UV coordinates by texture width and height
		int decalTexOriginX = (int)(uvX * ((float)baseMap.width));
		int decalTexOriginY = (int)(uvY * ((float)baseMap.height));
		var alteredMap = DrawTextureOntoTexture(baseMap, textureToDraw, decalTexOriginX, decalTexOriginY, blendFunction);

		material.SetTexture(textureMaterialPropertyName, alteredMap);
	}

	/// <param name="decalOriginX">Center x in pixels to draw the decal at</param>
	/// <param name="decalOriginY">Center y in pixels to draw the decal at</param>
	private static Texture2D DrawTextureOntoTexture(Texture2D originalTex, Texture2D decalToDraw, int decalOriginX, int decalOriginY, Func<Color, Color, Color> blendFunction)
	{
		Texture2D returnTex = new Texture2D(originalTex.width, originalTex.height, originalTex.graphicsFormat, mipCount: originalTex.mipmapCount, UnityEngine.Experimental.Rendering.TextureCreationFlags.DontInitializePixels);
		Graphics.CopyTexture(originalTex, returnTex);

		Color[] decalTexPixels = decalToDraw.GetPixels();
		Color[] originalTexPixels = originalTex.GetPixels();

		//cache to limit C++ interop
		int originalTexWidth = originalTex.width;
		int originalTexHeight = originalTex.height;

		int decalHeight = decalToDraw.height;
		int decalWidth = decalToDraw.width;

		int decalHalfHeight = (int)Mathf.Ceil(decalHeight / 2f);
		int decalHalfWidth = (int)Mathf.Ceil(decalWidth / 2f);

		int bottomLeftCornerDecalX = decalOriginX - decalHalfWidth;
		int bottomLeftCornerDecalY = decalOriginY - decalHalfHeight;

		for (int y = 0; y < decalHeight; y++)
		{
			for (int x = 0; x < decalWidth; x++)
			{
				int xToDrawAt = bottomLeftCornerDecalX + x;
				int yToDrawAt = bottomLeftCornerDecalY + y;

				//don't draw off the texture
				if (xToDrawAt < 0 || yToDrawAt < 0 || xToDrawAt > (originalTexWidth - 1) || yToDrawAt > (originalTexHeight - 1))
					continue;

				int originalTexIndex = xToDrawAt + (yToDrawAt * originalTexWidth);

				var decalColor = decalTexPixels[x + (y * decalWidth)];
				var originalTexColor = originalTexPixels[originalTexIndex];

				//Debug.Log($"OriginalTex color: {originalTexPixels[originalTexIndex]} Decal color: {decalColor}, final color: {finalColor}");
				//Debug.Log($"Setting originalTex pixel at x: {xToDrawAt}, y: {yToDrawAt}, Decal index x: {x} y: {y}");
				originalTexPixels[originalTexIndex] = blendFunction(originalTexColor, decalColor);
			}
		}
		returnTex.SetPixels(originalTexPixels, 0);
		returnTex.Apply();
		return returnTex;
	}

	//Simple lerp between r, g, b, using alpha as blend scalar
	private static Color BlendAlbedoColor(Color originalColor, Color newColor)
	{
		return new Color(Mathf.Lerp(originalColor.r, newColor.r, newColor.a), Mathf.Lerp(originalColor.g, newColor.g, newColor.a),
					Mathf.Lerp(originalColor.b, newColor.b, newColor.a), originalColor.a);
	}

	//Lerp normal colors using alpha as blend scalar
	//Because of DTX5nm format, we need to ignore the r and b channels, and set the alpha channel to the decal's r channel
	private static Color BlendNormalMaps(Color originalColor, Color newColor)
	{
		return new Color(originalColor.r, Mathf.Lerp(originalColor.g, newColor.g, newColor.a), originalColor.b,
					Mathf.Lerp(originalColor.a, newColor.r, newColor.a));
	}

	private static Color PartialDerivativeBlend(Color color1, Color color2)
	{
		float3 n1 = (ColorToFloat3(color1).xyz * 2) - 1;
		float3 n2 = (ColorToFloat3(color2).xyz * 2) - 1;
		float3 r = math.normalize(new float3((n1.xy * n2.z) + (n2.xy * n1.z), n1.z * n2.z));

		return Float3ToColor((r * 0.5f) + 0.5f);
	}

	//This doesn't work for finding the nearest UV. Need to do more research on the math.
	private static Vector2 FindNearestUV(Mesh mesh, Vector3 hitPoint)
	{
		int[] triangles = mesh.triangles;
		Vector3[] vertices = mesh.vertices;
		Vector2[] uvs = mesh.uv;

		float closestDistanceSqr = Mathf.Infinity;
		Vector2 closestUV = Vector2.zero;

		for (int i = 0; i < triangles.Length; i += 3)
		{
			// Get the vertices of the triangle
			Vector3 v0 = vertices[triangles[i]];
			Vector3 v1 = vertices[triangles[i + 1]];
			Vector3 v2 = vertices[triangles[i + 2]];

			// Find the closest point on the triangle to the hit point
			Vector3 closestPoint = ClosestPointOnTriangle(hitPoint, v0, v1, v2);
			float distanceSqr = (hitPoint - closestPoint).sqrMagnitude;

			if (distanceSqr < closestDistanceSqr)
			{
				closestDistanceSqr = distanceSqr;

				// Interpolate UV coordinates based on the closest point
				Vector2 uv0 = uvs[triangles[i]];
				Vector2 uv1 = uvs[triangles[i + 1]];
				Vector2 uv2 = uvs[triangles[i + 2]];

				Vector3 barycentricCoord = Barycentric(closestPoint, v0, v1, v2);

				closestUV = uv0 * barycentricCoord.x + uv1 * barycentricCoord.y + uv2 * barycentricCoord.z;
			}
		}

		return closestUV;
	}

	private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
	{
		Vector3 ab = b - a;
		Vector3 ac = c - a;
		Vector3 ap = p - a;

		float d1 = Vector3.Dot(ab, ap);
		float d2 = Vector3.Dot(ac, ap);

		if (d1 <= 0f && d2 <= 0f) return a;

		Vector3 bp = p - b;
		float d3 = Vector3.Dot(ab, bp);
		float d4 = Vector3.Dot(ac, bp);

		if (d3 >= 0f && d4 <= d3) return b;

		float vc = d1 * d4 - d3 * d2;
		if (vc <= 0f && d1 >= 0f && d3 <= 0f)
		{
			float v = d1 / (d1 - d3);
			return a + v * ab;
		}

		Vector3 cp = p - c;
		float d5 = Vector3.Dot(ab, cp);
		float d6 = Vector3.Dot(ac, cp);

		if (d6 >= 0f && d5 <= d6) return c;

		float vb = d5 * d2 - d1 * d6;
		if (vb <= 0f && d2 >= 0f && d6 <= 0f)
		{
			float w = d2 / (d2 - d6);
			return a + w * ac;
		}

		float va = d3 * d6 - d5 * d4;
		if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
		{
			float u = (d4 - d3) / ((d4 - d3) + (d5 - d6));
			return b + u * (c - b);
		}

		return a + ab * (d1 / (d1 + d3)) + ac * (d2 / (d2 + d6));
	}

	private static Vector3 Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
	{
		Vector3 v0 = b - a;
		Vector3 v1 = c - a;
		Vector3 v2 = p - a;
		float d00 = Vector3.Dot(v0, v0);
		float d01 = Vector3.Dot(v0, v1);
		float d11 = Vector3.Dot(v1, v1);
		float d20 = Vector3.Dot(v2, v0);
		float d21 = Vector3.Dot(v2, v1);
		float denom = d00 * d11 - d01 * d01;
		float v = (d11 * d20 - d01 * d21) / denom;
		float w = (d00 * d21 - d01 * d20) / denom;
		float u = 1.0f - v - w;
		return new Vector3(u, v, w);
	}

	private static Color Float3ToColor(float3 float3) => new Color(float3.x, float3.y, float3.z);

	private static float3 ColorToFloat3(Color color) => new float3(color.r, color.g, color.b);

	private static Color32 AddColors32(Color32 color1, Color32 color2) => new Color32((byte)(color1.r + color2.r), (byte)(color1.g + color2.g), (byte)(color1.b + color2.b), 1);

	private static bool TryGetMaterialAndRenderer(GameObject gameObject, out Material material, out Renderer renderer)
	{
		material = null;
		renderer = null;

		MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
		if (meshFilter == null)
			return false;

		Mesh mesh = meshFilter.sharedMesh ?? meshFilter.mesh;
		if (mesh == null || !mesh.isReadable)
			return false;

		renderer = meshFilter.GetComponent<Renderer>();
		if (renderer == null)
			return false;

		material = renderer.material;
		if (material == null)
			return false;

		return true;
	}

	private static bool TryGetSubmeshMaterial(RaycastHit hit, out Material material, out Renderer renderer)
	{
		material = null;
		renderer = null;

		MeshFilter meshFilter = hit.collider.GetComponent<MeshFilter>();
		if (meshFilter == null)
			return false;

		Mesh mesh = meshFilter.sharedMesh ?? meshFilter.mesh;
		if (mesh == null || !mesh.isReadable)
			return false;

		renderer = meshFilter.GetComponent<Renderer>();
		if (renderer == null)
			return false;

		Material[] materials = renderer.materials;

		if (materials.Length > 1)
			return false;

		int subMeshIndex = GetSubMeshIndex(meshFilter.sharedMesh, hit.triangleIndex);

		Debug.Log("submeshIndex is " + subMeshIndex);

		int materialIndex;

		// If there are more materials than there are sub-meshes, Unity renders the last sub-mesh with each of the remaining materials, one on top of the next.
		if (materials.Length > mesh.subMeshCount && subMeshIndex == mesh.subMeshCount - 1)
			materialIndex = materials.Length - 1;
		else
			materialIndex = subMeshIndex;

		material = materials.ElementAtOrDefault(materialIndex);
		return true;
	}

	private static int GetSubMeshIndex(in Mesh mesh, int triangleIndex)
	{
		for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
		{
			int[] subMeshTriangles = mesh.GetTriangles(subMeshIndex);

			for (int i = 0; i < subMeshTriangles.Length; i += 3)
			{
				if (subMeshTriangles[i] == mesh.triangles[triangleIndex * 3] &&
				subMeshTriangles[i + 1] == mesh.triangles[(triangleIndex * 3) + 1] &&
				subMeshTriangles[i + 2] == mesh.triangles[(triangleIndex * 3) + 2])
				{
					return subMeshIndex;
				}
			}
		}

		return -1;
	}

	//3D
	//Vector3 objectSpaceBulletHitPos = ConvertWorldSpaceToObjectSpace(hit.point, renderer);
	//Texture3D bulletLocationsTex = (Texture3D)material.GetTexture("_BulletMap");
	//bulletLocationsTex.SetPixel(objectSpaceBulletHitPos.x, objectSpaceBulletHitPos.y, objectSpaceBulletHitPos.z, Color.red);

	public static Texture3D Draw3DTextureOntoOther3DTexture(Texture3D originalTex, Texture3D decalToDraw, int decalOriginX, int decalOriginY, int decalOriginZ)
	{
		Texture3D returnTex = new Texture3D(originalTex.width, originalTex.height, originalTex.depth, originalTex.graphicsFormat, flags: UnityEngine.Experimental.Rendering.TextureCreationFlags.DontInitializePixels, mipCount: 0);
		Graphics.CopyTexture(originalTex, returnTex);

		Color[] decalTexPixels = decalToDraw.GetPixels();
		Color[] originalTexPixelsCopy = originalTex.GetPixels();

		//TEST:

		//for (int i = 0; i < originalTexPixels.Length; i++)
		//{
		//	originalTexPixels[i] = Color.red;
		//}
		//returnTex.SetPixels(originalTexPixels, 0);
		//returnTex.Apply();
		//return returnTex;

		//

		int originalTexWidth = returnTex.width; //cache to limit C++ calls
		int originalTexHeight = returnTex.height; //cache to limit C++ calls
		int originalTexDepth = returnTex.depth; //cache to limit C++ calls

		int decalWidth = decalToDraw.width;
		int decalHeight = decalToDraw.height;
		int decalDepth = decalToDraw.depth;

		int decalHalfWidth = (int)Mathf.Ceil(decalWidth / 2f);
		int decalHalfHeight = (int)Mathf.Ceil(decalHeight / 2f);
		int decalHalfDepth = (int)Mathf.Ceil(decalDepth / 2f);

		int bottomLeftCornerDecalX = decalOriginX - decalHalfWidth;
		int bottomLeftCornerDecalY = decalOriginY - decalHalfHeight;
		int bottomLeftCornerDecalZ = decalOriginZ - decalHalfDepth;

		for (int z = 0; z < decalDepth; z++)
		{
			for (int y = 0; y < decalHeight; y++)
			{
				for (int x = 0; x < decalWidth; x++)
				{
					int xToDrawAt = bottomLeftCornerDecalX + x;
					int yToDrawAt = bottomLeftCornerDecalY + y;
					int zToDrawAt = bottomLeftCornerDecalZ + z;

					//don't draw off the texture
					if (xToDrawAt < 0 || yToDrawAt < 0 || zToDrawAt < 0 || xToDrawAt > (originalTexWidth - 1) || yToDrawAt > (originalTexHeight - 1) || zToDrawAt > (originalTexDepth - 1))
						continue;

					int originalTexIndex = xToDrawAt + (yToDrawAt * originalTexWidth) + (zToDrawAt * originalTexWidth * originalTexHeight);

					var decalColor = decalTexPixels[x + (y * decalWidth) + (z * decalWidth * decalHeight)];
					//Lerp alpha
					//decalColor = Color.Lerp(originalTexPixels[originalTexIndex], decalColor, decalColor.a);

					originalTexPixelsCopy[originalTexIndex] = decalColor;
				}
			}
		}
		returnTex.SetPixels(originalTexPixelsCopy, 0);
		returnTex.Apply();
		return returnTex;
	}

	private static Vector3 ConvertWorldSpaceToObjectSpace(Vector3 worldSpacePoint, Renderer objectToCenterOn)
	{
		Vector3 localPosition = objectToCenterOn.transform.InverseTransformPoint(worldSpacePoint);

		Bounds rendererBounds = objectToCenterOn.bounds;
		Vector3 objectSpacePosition = DivideVector3(localPosition, rendererBounds.max);
		if (objectSpacePosition.x > 1 || objectSpacePosition.y > 1 || objectSpacePosition.z > 1)
		{
			Debug.LogError($"Point {worldSpacePoint} is outside bounds of renderer {objectToCenterOn.gameObject.name}");
			return Vector3.zero;        //TODO: make this a safer return
		}
		Debug.Log("Object space position is " + objectSpacePosition);
		return objectSpacePosition;
	}

	private static Vector3 DivideVector3(Vector3 numerator, Vector3 denominator) => new Vector3(numerator.x / denominator.x, numerator.y / denominator.y, numerator.z / denominator.z);

	private static Vector3 MapVector3ToRange(Vector3 input, Vector3 inputMin, Vector3 inputMax, Vector3 outputMin, Vector3 outputMax)
	{
		float x = (float)MapValueToValue(input.x, inputMin.x, inputMax.x, outputMin.x, outputMax.x);
		float y = (float)MapValueToValue(input.y, inputMin.y, inputMax.y, outputMin.y, outputMax.y);
		float z = (float)MapValueToValue(input.z, inputMin.z, inputMax.z, outputMin.z, outputMax.z);
		return new Vector3(x, y, z);
	}

	private static double MapValueToValue(float input, float inputMin, float inputMax, float outputMin, float outputMax)
	{
		double slope = 1.0 * (outputMax - outputMin) / (inputMax - inputMin);
		return outputMin + slope * (input - inputMin);
	}
}

public static class BaryCentricDistance
{
	public struct Result
	{
		public float distanceSquared;
		//This is usually unnecessary, so save some cycles by just having a function pointer instead of doing the sqrt on Result construction
		public float Distance => Mathf.Sqrt(distanceSquared);

		public int triangle;
		public Vector2[] triangleUVs;   //3 vec2s
		public Vector3 normal;
		public Vector3 triCenter;
		public Vector3 barycentricCenter;
		public Vector3 closestPoint;
	}

	public static Result GetClosestTriangleAndPoint(Vector3 worldSpacePoint, Transform meshTransform, Mesh mesh)
	{
		int[] triangles = mesh.triangles;
		Vector2[] uvs = mesh.uv;
		Vector3[] vertices = mesh.vertices;

		//Transform world point into local space so that all distance calcs can be done in local space
		var localPoint = meshTransform.InverseTransformPoint(worldSpacePoint);
		var minDistance = float.PositiveInfinity;
		var finalResult = new Result();
		var length = (int)(triangles.Length);
		for (var t = 0; t < length; t += 3)
		{
			var result = GetTriangleInfoForPoint(localPoint, t, vertices, uvs, triangles);
			if (result.distanceSquared < minDistance)
			{
				minDistance = result.distanceSquared;
				finalResult = result;
				//Debug.Log($"closest triangle is: {t}, distance is {finalResult.Distance}, closestPoint is {finalResult.closestPoint}");
			}
		}

		//Transform back to world space when we find the correct triangle
		finalResult.triCenter = meshTransform.TransformPoint(finalResult.triCenter);
		finalResult.closestPoint = meshTransform.TransformPoint(finalResult.closestPoint);
		finalResult.normal = meshTransform.TransformDirection(finalResult.normal);
		finalResult.distanceSquared = (finalResult.closestPoint - localPoint).sqrMagnitude;
		return finalResult;
	}

	private static Result GetTriangleInfoForPoint(Vector3 point, int triangle, Vector3[] vertices, Vector2[] uvs, int[] triangles)
	{
		Result result = new Result();

		result.triangle = triangle;
		result.distanceSquared = float.PositiveInfinity;

		if (triangle >= triangles.Length / 3)
			return result;

		//Get the vertices of the triangle
		var p1 = vertices[triangles[0 + triangle]];
		var p2 = vertices[triangles[1 + triangle]];
		var p3 = vertices[triangles[2 + triangle]];

		//Debug.Log($"p1 {p1} p2 {p2} p3 {p3}");

		var p1Uv = uvs[triangles[0 + triangle]];
		var p2Uv = uvs[triangles[1 + triangle]];
		var p3Uv = uvs[triangles[2 + triangle]];

		result.triCenter = p1 * 0.3333f + p2 * 0.3333f + p3 * 0.3333f;
		result.normal = CalculateSurfaceNormal(p1, p2, p3);

		//Project our point onto the plane
		//var projected = point + Vector3.Dot((p1 - point), result.normal) * result.normal;
		var n = result.normal;
		var o = point - result.triCenter;
		var dist = o.x * n.x + o.y * n.y + o.z * n.z;
		var projected = point - (dist * n);

		//Calculate the barycentric coordinates
		//Cross product
		float areaOfParallelogram = Vector3.Cross(p2 - p1, p3 - p1).magnitude;   //(p1.x * p2.y) - (p1.x * p3.y) - (p2.x * p1.y) + (p2.x * p3.y) + (p3.x * p1.y) - (p3.x * p2.y);

		var u = Vector3.Cross(projected - p1, p3 - p1).magnitude / areaOfParallelogram;
		var v = Vector3.Cross(projected - p3, p2 - p3).magnitude / areaOfParallelogram;
		var w = Vector3.Cross(projected - p2, p1 - p2).magnitude / areaOfParallelogram;

		var debugU = Vector3.Cross(projected - p1, p3 - p1);
		var debugV = Vector3.Cross(projected - p3, p2 - p3);
		var debugW = Vector3.Cross(projected - p2, p1 - p2);

		//var u = ((projected.x * p2.y) - (projected.x * p3.y) - (p2.x * projected.y) + (p2.x * p3.y) + (p3.x * projected.y) - (p3.x * p2.y)) / areaOfParallelogram;
		//var v = ((p1.x * projected.y) - (p1.x * p3.y) - (projected.x * p1.y) + (projected.x * p3.y) + (p3.x * p1.y) - (p3.x * projected.y)) / areaOfParallelogram;
		//var w = ((p1.x * p2.y) - (p1.x * projected.y) - (p2.x * p1.y) + (p2.x * projected.y) + (projected.x * p1.y) - (projected.x * p2.y)) / areaOfParallelogram;

		if (u >= 0 && v >= 0 && w >= 0 && u + v + w == 1)
		{
			Debug.Log($"Point {point} is inside triangle {triangle}");
		}

		result.triangleUVs = new Vector2[] {
			u * p1Uv,
			v * p2Uv,
			w * p3Uv
		};

		//Find the nearest point in barycentric coordinates
		var barycentricPoint = (new Vector3(u, v, w)).normalized;
		result.barycentricCenter = barycentricPoint;

		//work out where that point is in local space
		var nearest = p1 * barycentricPoint.x + p2 * barycentricPoint.y + p3 * barycentricPoint.z;
		result.closestPoint = nearest;
		result.distanceSquared = (nearest - point).sqrMagnitude;

		if (float.IsNaN(result.distanceSquared))
		{
			result.distanceSquared = float.PositiveInfinity;
		}
		return result;
	}

	private static Vector3 CalculateSurfaceNormal(Vector3 p1, Vector3 p2, Vector3 p3)
	{
		Vector3 U = (p2 - p1);
		Vector3 V = (p3 - p1);

		Vector3 Normal = new Vector3(
			x: (U.y * V.z) - (U.z * V.y),
			y: (U.z * V.x) - (U.x * V.z),
			z: (U.x * V.y) - (U.y * V.x)
		);

		return Vector3.Normalize(Normal);
	}
}