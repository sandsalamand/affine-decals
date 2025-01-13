using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This class is simply a scene placeholder for DecalCustomInspector to work upon.
/// The Decal MonoBehaviour holds data which DecalCustomInspector uses to spawn decals in the scene when the Inspector button is clicked.
/// </summary>
public class Decal : MonoBehaviour
{
	public SimpleDecalData decalData;

	//Can't allow this because the shader can be placed onto objects of any layer
	//public LayerMask projectOnto;

	public float depth = 1f;
}
