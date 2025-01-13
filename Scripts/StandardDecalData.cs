using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(fileName = "StandardDecalData", menuName = "Standard Decal Data")]
public class StandardDecalData : DecalData
{
	[SerializeField]
	[Tooltip("Define how the decal will appear on each type of physics material.")]
	private SerializableDictionary<PhysicMaterial, Texture2D> decalData = new();

	[SerializeField]
	[Tooltip("Define how the decal will appear on each type of physics material.")]
	private SerializableDictionary<PhysicMaterial, AssetReferenceTexture> decalAssets = new();

	public override AssetReferenceTexture GetAssociatedDecal(PhysicMaterial physMaterial)
	{
		if (decalAssets == null || decalAssets.Count == 0)
		{
			Debug.LogError("DecalData dictionary is null or empty. This will cause errors.");
			return null;
		}

		if (decalAssets.TryGetValue(physMaterial, out AssetReferenceTexture texture))
			return texture;

		return decalAssets.Values.First();	//default
	}

	public override IEnumerable<Texture2D> GetAllTextures() => decalData.Values;
}