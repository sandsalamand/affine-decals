#define DEBUGGING

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AddressableAssets;

//The intention of this class was to be completely static and not rely on a scene GameObject.
//However, I found that the only way to get a reliable callback after the initial scene loads upon opening the Editor is to use the Awake of ExecuteInEditMode.
//Awake is the only MonoBehaviour functionality used.
[ExecuteInEditMode]
public class DecalGlobalController: MonoBehaviour
{
	private const int decalTextureMaxWidth = 256;
	private const TextureFormat textureFormat = TextureFormat.RGBA32;

	private const int runtimeDecalsMax = 236;
	private const int permanentDecalsMax = 20;
	private const int decalArrayDepth = 50;

	/// <summary> These decals are created at runtime and cleared when play mode exits. </summary>
	private static DecalsInformation runtimeDecals = new DecalsInformation(runtimeDecalsMax, decalArrayDepth);
	/// <summary> These decals are created in the Editor and persist between plays. The correct decal data is loaded based on the Scene that it's associated with. </summary>
	private static DecalsInformation permanentDecals = new DecalsInformation(permanentDecalsMax, decalArrayDepth);

	//=============== TextureArray2D Data ===============
	//This data is shared between runtime decals and permanent decals, because both logical structures work upon the same underlying GPU resource of the Texture2DArray

	/// <summary> Provides an accelerated way to look up texture indices, as opposed to a linear search of the SizedQueue </summary>
	private static Dictionary<Texture2D, int> indicesOfTexturesInTex2DArray;
	/// <summary> Keeps track of the number of assigned textures inside the tex array. </summary>
	private static int currentTex2DArrayIndex;
	/// <summary> Stores a reference to the Texture2DArray which is stored on the GPU. Not readable on CPU. Graphics.Copy is used to interact with this.</summary>
	private static Texture2DArray DecalArrayGPUData { get; set; }
	//===================================================

	public class DecalsInformation
	{
		internal int maxDecalQuantity;
		internal int maxDecalTextures;

		internal SizedQueue<Vector4> decalPositions;
		internal SizedQueue<Vector4> decalNormals;
		internal SizedQueue<Vector4> decalTangents;
		internal SizedQueue<Texture2D> decalAlbedoTextures;
		internal SizedQueue<AssetReferenceTexture> decalAlbedoTextureAssets;

		/// <summary>
		/// Construct an empty DecalsInformation
		/// </summary>
		internal DecalsInformation(int maxDecalQuantity, int maxDecalTextures)
		{
			this.maxDecalQuantity = maxDecalQuantity;
			this.maxDecalTextures = maxDecalTextures;
			this.decalPositions = new SizedQueue<Vector4>(maxDecalQuantity);
			this.decalNormals = new SizedQueue<Vector4>(maxDecalQuantity);
			this.decalTangents = new SizedQueue<Vector4>(maxDecalQuantity);
			this.decalAlbedoTextures = new(maxDecalTextures);
			this.decalAlbedoTextureAssets = new(maxDecalTextures);
		}

		/// <summary>
		/// Deserialize decal data into a DecalsInformation instance
		/// </summary>
		internal DecalsInformation(SerializableData serializableData)
		{
			this.maxDecalQuantity = serializableData.maxDecalQuantitySerialized;
			this.maxDecalTextures = serializableData.maxDecalTexturesSerialized;
			this.decalPositions = new SizedQueue<Vector4>(this.maxDecalQuantity, serializableData.positionsSerialized);
			this.decalNormals = new SizedQueue<Vector4>(this.maxDecalQuantity, serializableData.normalsSerialized);
			this.decalTangents = new SizedQueue<Vector4>(this.maxDecalQuantity, serializableData.tangentsSerialized);
			IEnumerable<Texture2D> texturesDeserialized = serializableData.texturesSerialized.Select(data =>
			{
				var asyncHandle = data.LoadAssetAsync<Texture2D>();
				Texture2D tex = asyncHandle.WaitForCompletion();
				return tex;

			});
			this.decalAlbedoTextures = new SizedQueue<Texture2D>(this.maxDecalTextures, texturesDeserialized);
			this.decalAlbedoTextureAssets = new SizedQueue<AssetReferenceTexture>(this.maxDecalTextures, serializableData.texturesSerialized);
		}

		internal void Reset()
		{
			this.decalPositions = new SizedQueue<Vector4>(this.maxDecalQuantity);
			this.decalNormals = new SizedQueue<Vector4>(this.maxDecalQuantity);
			this.decalTangents = new SizedQueue<Vector4>(this.maxDecalQuantity);
			this.decalAlbedoTextures = new(this.maxDecalTextures);
			this.decalAlbedoTextureAssets = new(this.maxDecalTextures);
		}

		internal SerializableData ToSerializable() => new SerializableData
		{
			maxDecalQuantitySerialized = maxDecalQuantity,
			maxDecalTexturesSerialized = maxDecalTextures,

			positionsSerialized = decalPositions.ToArray(),
			normalsSerialized = decalNormals.ToArray(),
			tangentsSerialized = decalTangents.ToArray(),
			texturesSerialized = decalAlbedoTextureAssets.ToArray()
		};

		//Used to preserve data between assembly reloads in the Editor. Also used when loading scenes at runtime
		[Serializable]
		public struct SerializableData
		{
			public int maxDecalQuantitySerialized;
			public int maxDecalTexturesSerialized;

			public Vector4[] positionsSerialized;
			public Vector4[] normalsSerialized;
			public Vector4[] tangentsSerialized;
			public AssetReferenceTexture[] texturesSerialized;
		}
	}

	private struct Texture2DArrayData
	{
		public Texture2DArray texArray;
		//Provides an accelerated way to look up texture indices, as opposed to a linear search of the SizedQueue
		public Dictionary<Texture2D, int> textureIndexLookup;
		//This represents the number of assigned textures inside the tex array.
		public int textureQuantity;

		public Texture2DArrayData(IEnumerable<Texture2D> textures)
		{
			this.texArray = InitTexture2DArray();
			this.textureQuantity = 0;
			this.textureIndexLookup = new();

			foreach (Texture2D tex in textures)
			{
				int lastTexIndex = textureQuantity;
				Graphics.CopyTexture(tex, 0, 0, srcX: 0, srcY: 0, tex.width, tex.height, dst: texArray, dstElement: lastTexIndex, dstMip: 0, dstX: 0, dstY: 0);
				this.textureIndexLookup.Add(tex, lastTexIndex);
				this.textureQuantity++;
			}
		}
	}

	private void OnEnable()
	{
		Debug.Log("DecalGlobalController OnEnable");
		TryLoadActiveSceneDecals();

#if UNITY_EDITOR
		//Attach Editor callbacks if in edit mode
		if (!Application.isPlaying)
		{
			EditorSceneManager.sceneClosed += SceneClosed;
			EditorSceneManager.sceneOpened += SceneOpened;
			EditorApplication.playModeStateChanged += PlaymodeStateChanged;
		}
#endif
	}

	private static void TryLoadActiveSceneDecals()
	{
		Scene scene = SceneManager.GetActiveScene();

		if (!string.IsNullOrEmpty(scene.name))
		{
			Debug.Log("InitializeOnLoad scene is " + scene.name);
			ResetToPermanentDecalsOnly(scene);
		}
	}

	/// <summary>
	/// Reset runtime decals and set all globals to use the permanent decals set in the Editor
	/// </summary>
	private static void ResetToPermanentDecalsOnly(Scene scene)
	{
		runtimeDecals.Reset();

		permanentDecals = LoadPermanentDecals(scene);

		var newTexArrayData = new Texture2DArrayData(permanentDecals.decalAlbedoTextures);

		SetShaderBuffers();
		Shader.SetGlobalTexture("_DecalAlbedoArray", newTexArrayData.texArray);

		DecalArrayGPUData = newTexArrayData.texArray;
		indicesOfTexturesInTex2DArray = newTexArrayData.textureIndexLookup;
		currentTex2DArrayIndex = newTexArrayData.textureQuantity;
	}

#if UNITY_EDITOR
	//The Texture2DArray gets cleared from GPU memory when play mode exits, so we have to set it again
	private static void PlaymodeStateChanged(PlayModeStateChange playModeStateChange)
	{
		if (playModeStateChange == PlayModeStateChange.EnteredEditMode)
		{
			Debug.Log("Entered Edit Mode");
			ResetToPermanentDecalsOnly(SceneManager.GetActiveScene());
		}
	}

	private static void SceneOpened(Scene scene, OpenSceneMode openSceneMode)
	{
		Debug.Log("Scene loaded!");
		//ResetToPermanentDecalsOnly(scene);
	}

	private static void SceneClosed(Scene scene)
	{
		//Not sure if the below line is necessary to prevent a memory leak.
		//Destroy(DecalArrayGPUData);
	}
#endif

	//TODO: We need to use Application.streamingAssetsPath to ensure that we can load at runtime in a standalone build. Can't use Scene folders
	private static DecalsInformation LoadPermanentDecals(Scene scene)
	{
		string sceneDecalDataPath = GetDecalSceneDataPath(scene);

		if (!File.Exists(sceneDecalDataPath))
		{
			Debug.Log("Cannot load decal data, because it does not exist.");
			return new DecalsInformation(permanentDecalsMax, decalArrayDepth);
		}

		Debug.Log("Loading scene decal data, scene name is " + scene.name);
		
		string jsonDecalData = File.ReadAllText(sceneDecalDataPath);
		var deserializedData = JsonUtility.FromJson<DecalsInformation.SerializableData>(jsonDecalData);

        return new DecalsInformation(deserializedData);
	}

	private static Texture2DArray InitTexture2DArray()
	{
		var newArray = new Texture2DArray(decalTextureMaxWidth, decalTextureMaxWidth, decalArrayDepth, textureFormat, mipChain: false, false, createUninitialized: true);

		newArray.name = "DecalAlbedoArray";
		newArray.wrapMode = TextureWrapMode.Clamp;
		newArray.filterMode = FilterMode.Point;

		newArray.Apply(updateMipmaps: false, makeNoLongerReadable: true);

		return newArray;
	}

	private static int AddDecalTextureToGlobalShaderTexArray(Texture2D decalTex)
	{
		int lastTexIndex = currentTex2DArrayIndex;
		Graphics.CopyTexture(decalTex, 0, 0, srcX: 0, srcY: 0, decalTex.width, decalTex.height, dst: DecalArrayGPUData, dstElement: lastTexIndex, dstMip: 0, dstX: 0, dstY: 0);
		indicesOfTexturesInTex2DArray.Add(decalTex, lastTexIndex);
		currentTex2DArrayIndex++;

		Shader.SetGlobalTexture("_DecalAlbedoArray", DecalArrayGPUData);

		return lastTexIndex;
	}

	public static void TryDrawDecalOnMeshRuntime(RaycastHit hit, DecalData decalData)
	{
		MeshFilter meshFilter = hit.collider.gameObject.GetComponent<MeshFilter>();
		if (meshFilter == null)
			return;
		Mesh mesh = meshFilter.sharedMesh;
		if (mesh == null)
			return;

		DrawRuntimeDecalOnMesh(hit.collider.sharedMaterial, decalData, hit.point, hit.normal, hit.barycentricCoordinate, hit.triangleIndex, mesh, hit.collider.transform);
	}

	public static void DrawRuntimeDecalOnMesh(PhysicMaterial hitMaterial, DecalData decalToDraw, Vector3 worldPosition, Vector3 normal, Vector3 barycentricHitPos, int hitTriangleIndex, Mesh mesh, Transform meshTransform)
	{
		AssetReferenceTexture decalTextureAsset = decalToDraw.GetAssociatedDecal(hitMaterial);
		Texture2D decalTexture;
		if (decalTextureAsset.Asset == null)
		{
			var asyncHandle = decalTextureAsset.LoadAssetAsync<Texture2D>();
			decalTexture = asyncHandle.WaitForCompletion();
		}
		else
			decalTexture = (Texture2D)decalTextureAsset.Asset;

		int decalIndex;

		//If the decal texture did not already exist in the dictionary, then add it to the Texture2DArray and to the texture SizedQueue
		if (indicesOfTexturesInTex2DArray.TryGetValue(decalTexture, out decalIndex) == false)
		{
			decalIndex = AddDecalTextureToGlobalShaderTexArray(decalTexture);
			runtimeDecals.decalAlbedoTextures.Enqueue(decalTexture);
		}

		runtimeDecals.decalPositions.Enqueue(new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, decalIndex));

		//We pack decal width into the w channel of the vector4 because Unity only allows us to pass vec4 or vec2 arrays to the shader
		runtimeDecals.decalNormals.Enqueue(new Vector4(normal.x, normal.y, normal.z, decalTexture.width));

		Vector3 tangent = GetTangentOfPoint(barycentricHitPos, mesh.tangents, mesh.triangles, hitTriangleIndex, meshTransform);

		//Pack decal height into w channel
		runtimeDecals.decalTangents.Enqueue(new Vector4(tangent.x, tangent.y, tangent.z, decalTexture.height));

		SetShaderBuffers();
	}

#if UNITY_EDITOR
	public static void TryDrawDecalOnMeshEditor(RaycastHit hit, DecalData decalData)
	{
		MeshFilter meshFilter = hit.collider.gameObject.GetComponent<MeshFilter>();
		if (meshFilter == null)
			return;
		Mesh mesh = meshFilter.sharedMesh;
		if (mesh == null)
			return;

		DrawPermanentDecalOnMesh(hit.collider.sharedMaterial, decalData, hit.point, hit.normal, hit.barycentricCoordinate, hit.triangleIndex, mesh, hit.collider.transform);
	}

	public static void DrawPermanentDecalOnMesh(PhysicMaterial hitMaterial, DecalData decalToDraw, Vector3 worldPosition, Vector3 normal, Vector3 barycentricHitPos, int hitTriangleIndex, Mesh mesh, Transform meshTransform)
	{
		Debug.Log("Drawing permanent decal");

		AssetReferenceTexture decalTextureAsset = decalToDraw.GetAssociatedDecal(hitMaterial);
		Texture2D decalTexture;
		if (decalTextureAsset.Asset == null)
		{
			var asyncHandle = decalTextureAsset.LoadAssetAsync<Texture2D>();
			decalTexture = asyncHandle.WaitForCompletion();
		}
		else
			decalTexture = (Texture2D)decalTextureAsset.Asset;

		int decalIndex;

		//If the decal texture did not already exist in the dictionary, then add it to the Texture2DArray and to the texture SizedQueue
		if (indicesOfTexturesInTex2DArray.TryGetValue(decalTexture, out decalIndex) == false)
		{
			decalIndex = AddDecalTextureToGlobalShaderTexArray(decalTexture);
			permanentDecals.decalAlbedoTextures.Enqueue(decalTexture);
			permanentDecals.decalAlbedoTextureAssets.Enqueue(decalTextureAsset);
		}

		permanentDecals.decalPositions.Enqueue(new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, decalIndex));

		//We pack decal width into the w channel of the vector4 because Unity only allows us to pass vec4 or vec2 arrays to the shader
		permanentDecals.decalNormals.Enqueue(new Vector4(normal.x, normal.y, normal.z, decalTexture.width));

		Vector3 tangent = GetTangentOfPoint(barycentricHitPos, mesh.tangents, mesh.triangles, hitTriangleIndex, meshTransform);

		//Pack decal height into w channel
		permanentDecals.decalTangents.Enqueue(new Vector4(tangent.x, tangent.y, tangent.z, decalTexture.height));

		SetShaderBuffers();

		string jsonDecalData = JsonUtility.ToJson(permanentDecals.ToSerializable());

		string sceneDataPath = GetDecalSceneDataPath(SceneManager.GetActiveScene());

		File.WriteAllText(sceneDataPath, jsonDecalData); 
	}
#endif

	/// <summary>
	/// Creates a scene folder if one does not exist, but does not create the actual scene data file. Check to make sure it exists before using it.
	/// </summary>
	/// <returns>The path to a potentially existing scene-specific decal data file.</returns>
	static string GetDecalSceneDataPath(Scene scene)
	{
		string scenePath = scene.path;
		DirectoryInfo sceneFolder = Directory.GetParent(scenePath);

		string sceneDataFolder = Path.Combine(sceneFolder.FullName, scene.name) + Path.DirectorySeparatorChar;
		if (!Directory.Exists(sceneDataFolder))
		{
			Debug.Log("Created a new scene data folder at " + sceneDataFolder);
			Directory.CreateDirectory(sceneDataFolder);
		}

		return sceneDataFolder + "SceneDecalsData.json";
	}

	/// <summary>
	/// Sets shader buffers to a concatenation of permanent and runtime decals. Uses the fixed sizes of the arrays to match the shader buffer size.
	/// </summary>
	private static void SetShaderBuffers()
	{
		//These concatenations are extremely inefficient. Need to optmize later
		//emptyValue matters because the shader buffer is full of these values
		Shader.SetGlobalVectorArray("_BulletDecalPositions", permanentDecals.decalPositions.ToFixedArray(emptyValue: Vector4.positiveInfinity)
			.Concat(runtimeDecals.decalPositions.ToFixedArray(emptyValue: Vector4.positiveInfinity)).ToList());
		Shader.SetGlobalVectorArray("_BulletDecalNormals", permanentDecals.decalNormals.ToFixedArray(emptyValue: Vector4.zero)
			.Concat(runtimeDecals.decalNormals.ToFixedArray(emptyValue: Vector4.zero)).ToList());
		Shader.SetGlobalVectorArray("_BulletDecalTangents", permanentDecals.decalTangents.ToFixedArray(emptyValue: Vector4.zero)
			.Concat(runtimeDecals.decalTangents.ToFixedArray(emptyValue: Vector4.zero)).ToList());
	}

	private static Vector3 GetTangentOfPoint(Vector3 barycentricPos, Vector4[] tangents, int[] triangles, int triangleIndex, Transform hitTransform)
	{
		// Extract local space tangents of the triangle we hit
		Vector3 t0 = tangents[triangles[triangleIndex * 3 + 0]];    //implicit cast from vec4 to vec3
		Vector3 t1 = tangents[triangles[triangleIndex * 3 + 1]];
		Vector3 t2 = tangents[triangles[triangleIndex * 3 + 2]];

		// Use barycentric coordinate to interpolate normal
		Vector3 interpolatedTangent = t0 * barycentricPos.x + t1 * barycentricPos.y + t2 * barycentricPos.z;

		interpolatedTangent = interpolatedTangent.normalized;

		//Debug.Log("interpolatedTangent.x + interpolatedTangent.y + interpolatedTangent.z is " + interpolatedTangent.x + interpolatedTangent.y + interpolatedTangent.z);

		// Transform local space normals to world space
		interpolatedTangent = hitTransform.TransformDirection(interpolatedTangent);

		return interpolatedTangent;
	}
}