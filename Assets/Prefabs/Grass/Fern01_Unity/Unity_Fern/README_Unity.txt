THE AMBER RUINS - DARK ENCHANTED AUTUMN FERN

IMPORT INTO UNITY
1. Extract The_Amber_Ruins_Fern_Unity.zip.
2. Drag the entire Unity_Fern folder into Assets in the Unity Project window.
3. Drag The_Amber_Ruins_Fern.fbx from the Project window into your scene.

The FBX contains an embedded base-color texture. The Textures folder also
contains the original base-color and metallic-roughness textures and maps
prepared for common Unity material workflows. Shader settings depend on your
project's render pipeline. If textures are not assigned automatically, create
a material using the shader for your pipeline and assign the supplied maps.
Use a shader with double-sided foliage rendering.

TEXTURES
Fern_BaseColor.png - color map, sRGB enabled.
Fern_MetallicRoughness.png - original glTF PBR map; green is roughness and
blue is metallic. Disable sRGB for this data texture.
Fern_MetallicSmoothness.png - Unity metallic workflow: red is metallic and
alpha is smoothness. Disable sRGB and use the map alpha for smoothness.
Fern_HDRP_Mask.png - red metallic, green white ambient-occlusion multiplier,
blue white detail multiplier, alpha smoothness. Disable sRGB.
All supplied textures are 2048 x 2048 pixels.

VERIFICATION AND LIMITATIONS
The final binary FBX was re-imported with Assimp. It preserves 98,109 triangles,
normals, UVs, finite vertex positions, and the embedded color texture.
Unit metadata is meters. Approximate source dimensions: 0.60 m wide,
0.474 m tall, and 0.605 m deep. Its source origin is near the model center.
Import in the Unity Editor itself was not tested in this environment.

This is an unrigged asset. It contains no wind rig or animation clips.
The fronds are not delivered as individually named editable objects.
The generated geometry is NOT watertight. An inspection with exact-position
welding found 41 boundary edges, 1,715 non-manifold edges, and 9 zero-area
triangles. This inspection did not alter the mesh. Mesh cleanup is required
to meet the requested watertight specification; aggressive repair could
change the delicate leaf silhouettes and texture mapping.

The original Lux3D GLB is retained separately. GLB requires a glTF importer
in Unity; use this FBX for Unity's built-in model import workflow.
