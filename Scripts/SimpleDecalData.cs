using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(fileName = "SimpleDecalData", menuName = "Simple Decal Data")]
public class SimpleDecalData : DecalData
{
	[field: SerializeField]
	[Tooltip("This texture will be used on all physics materials.")]
	public Texture2D DecalTexture { get; set; }

	[field: SerializeField]
	[Tooltip("This texture will be used on all physics materials.")]
	public AssetReferenceTexture DecalTextureAsset { get; set; }

	public override AssetReferenceTexture GetAssociatedDecal(PhysicMaterial physMaterial) => DecalTextureAsset;

	public override IEnumerable<Texture2D> GetAllTextures() => Enumerable.Repeat(DecalTexture, 1);
}
