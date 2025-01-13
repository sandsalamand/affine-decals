using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
[CustomEditor(typeof(Decal), editorForChildClasses: false)]
public class DecalCustomInspector : Editor
{
	private const string buttonLabel = "Spawn Decal";

	//OnSceneGui()
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();

		Decal decal = target as Decal;
		if (decal == null)
			return;

		if (GUILayout.Button(buttonLabel))
		//if (decal.transform.hasChanged)
		{
			//decal.transform.hasChanged = false;

			//The only way this could work is by changing the Queue data structure for Editor decals to return a "ticket" integer
			//ModifyDecal(int index);

			RaycastHit hit;
			Ray ray = new Ray(origin: decal.transform.position, direction: decal.transform.forward);
			int layerMask = LayerRegistry.GetLayerMask(LayerRegistry.NavigationLayerId, 0);	//0 is Default layer
			bool didHit = Physics.Raycast(ray, out hit, decal.depth, layerMask);

			Debug.Log("did hit? " + didHit);

			if (didHit)
				DecalGlobalController.TryDrawDecalOnMeshEditor(hit, decal.decalData);
		}
	}
}
#endif