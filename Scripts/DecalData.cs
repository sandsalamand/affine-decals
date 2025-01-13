using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;

public abstract class DecalData : ScriptableObject
{
	public abstract AssetReferenceTexture GetAssociatedDecal(PhysicMaterial physMaterial);

	public abstract IEnumerable<Texture2D> GetAllTextures();
}