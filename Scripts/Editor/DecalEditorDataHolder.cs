using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
[FilePath("Assets/Textures/EditorDecalData.asset", FilePathAttribute.Location.ProjectFolder)]
public class DecalEditorDataHolder : ScriptableSingleton<DecalEditorDataHolder>, ISerializationCallbackReceiver
{
	[SerializeField] private Texture2DArray decalArrayGPUData;

	[SerializeField] private Vector4[] bulletPositionsSerialized;
	[SerializeField] private Texture2D[] texturesSerialized;

	public SizedQueue<Vector4> BulletPositions = new(0);
	public SizedQueue<Texture2D> Textures = new(0);

	//public SizedQueue<Texture2D> Textures
	//{
	//	get => textures;
	//	set
	//	{
	//		textures = value;
	//		Save(saveAsText: false);
	//	}
	//}

	//public SizedQueue<Vector4> BulletPositions
	//{
	//	get => bulletPositions;
	//	set
	//	{
	//		bulletPositions = value;
	//		Save(saveAsText: false);
	//	}
	//}

	public Texture2DArray DecalArrayGPUData
	{
		get => decalArrayGPUData;
		set
		{
			decalArrayGPUData = value;
			Save(saveAsText: false);
		}
	}

	public void OnBeforeSerialize()
	{
		texturesSerialized = Textures.ToArray();
		bulletPositionsSerialized = BulletPositions.ToArray();
	}

	public void OnAfterDeserialize()
	{
		Textures = new SizedQueue<Texture2D>(Textures.Count, texturesSerialized);
		BulletPositions = new SizedQueue<Vector4>(BulletPositions.Count, bulletPositionsSerialized);
	}

	private void OnEnable()
	{
		EditorApplication.playModeStateChanged += SaveData;
		EditorApplication.quitting += SaveData;
	}

	private void OnDisable()
	{
		EditorApplication.playModeStateChanged -= SaveData;
		EditorApplication.quitting -= SaveData;
	}

	private void SaveData() => Save(saveAsText: false);

	private void SaveData(PlayModeStateChange playModeStateChange)
	{
		if (playModeStateChange == PlayModeStateChange.ExitingPlayMode) { }
			SaveData();
	}
}
#endif