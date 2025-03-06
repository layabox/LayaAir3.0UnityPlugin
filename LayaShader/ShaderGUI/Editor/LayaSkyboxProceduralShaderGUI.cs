// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEditor;
using UnityEngine;

class LayaSkyboxProceduralShaderGUI : ShaderGUI
{
    private enum SunDiskMode
    {
        None,
        Simple,
        HighQuality
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
    {
        materialEditor.SetDefaultGUIWidths();

        MaterialProperty sunDiskModeProp = FindProperty("sunDisk", props);
        SunDiskMode sunDiskMode = (SunDiskMode)sunDiskModeProp.floatValue;

        for (var i = 0; i < props.Length; i++)
        {
            if ((props[i].flags & (MaterialProperty.PropFlags.HideInInspector | MaterialProperty.PropFlags.PerRendererData)) != 0)
                continue;

            //sunSizeConvergence is only used with the HighQuality sun disk.
            if ((props[i].name == "sunSizeConvergence") && (sunDiskMode != SunDiskMode.HighQuality))
                continue;

            float h = materialEditor.GetPropertyHeight(props[i], props[i].displayName);
            Rect r = EditorGUILayout.GetControlRect(true, h, EditorStyles.layerMaskField);

            materialEditor.ShaderProperty(r, props[i], props[i].displayName);
        }
    }
}
