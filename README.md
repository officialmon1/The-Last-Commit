# The Last Commit 🌲🍄🌿



## The Problem



## Our Solution



## Key Features



## Data



### Cost Estimation



## Cost Impact Methodology



## How It Works



## Tech Stack



## Project Context



## Limitations



## Data Source



## Project Contribution Team
Lingtong Meng, Monica Barbosa, Shirina Daniel & Zara Maraj

## Main Branch File Structure

```text
The-Last-Commit/
├── .gitignore
├── README.md
├── .vscode/
│   ├── extensions.json
│   ├── launch.json
│   └── settings.json
├── Assets/
│   ├── InputSystem_Actions.inputactions
│   ├── InputSystem_Actions.inputactions.meta
│   ├── Materials.meta
│   ├── Prefabs.meta
│   ├── Readme.asset
│   ├── Readme.asset.meta
│   ├── Scenes.meta
│   ├── Scripts.meta
│   ├── Settings.meta
│   ├── TutorialInfo.meta
│   ├── UI Toolkit.meta
│   ├── UI.meta
│   ├── Materials/
│   │   ├── AncientColumn01_BaseColor.png
│   │   ├── AncientColumn01_BaseColor.png.meta
│   │   ├── Birch_01.png
│   │   ├── Birch_01.png.meta
│   │   ├── Boulder01_BaseColor.png
│   │   ├── Boulder01_BaseColor.png.meta
│   │   ├── BrokenPillar_BaseColorpng.png
│   │   ├── BrokenPillar_BaseColorpng.png.meta
│   │   ├── Bush01_BaseColor.png
│   │   ├── Bush01_BaseColor.png.meta
│   │   ├── Fern01_BaseColor.png
│   │   ├── Fern01_BaseColor.png.meta
│   │   ├── Fern02_BaseColor.png
│   │   ├── Fern02_BaseColor.png.meta
│   │   ├── Flowers01_BaseColor.png
│   │   ├── Flowers01_BaseColor.png.meta
│   │   ├── GrassSmall_BaseColor.png
│   │   ├── GrassSmall_BaseColor.png.meta
│   │   ├── GrassTall_BaseColor.png
│   │   ├── GrassTall_BaseColor.png.meta
│   │   ├── MapleTree02_BaseColor.png
│   │   ├── MapleTree02_BaseColor.png.meta
│   │   ├── MossyRock01_BaseColor.png
│   │   ├── MossyRock01_BaseColor.png.meta
│   │   ├── Mushroom01_BaseColor.png
│   │   ├── Mushroom01_BaseColor.png.meta
│   │   ├── OakTree02_BaseColor.png
│   │   ├── OakTree02_BaseColor.png.meta
│   │   ├── OakTree_BaseColor.png
│   │   ├── OakTree_BaseColor.png.meta
│   │   ├── Panda_BaseColor.png
│   │   ├── Panda_BaseColor.png.meta
│   │   ├── TwistedTree01_BaseColor.png
│   │   ├── TwistedTree01_BaseColor.png.meta
│   │   ├── WillowBurgundy_BaseColor.png
│   │   ├── WillowBurgundy_BaseColor.png.meta
│   │   ├── WillowDeepGreen01_BaseColor.png
│   │   ├── WillowDeepGreen01_BaseColor.png.meta
│   │   ├── WillowGold_BaseColor.png
│   │   └── WillowGold_BaseColor.png.meta
│   ├── Prefabs/
│   │   ├── Characters/
│   │   │   ├── Panda.fbx
│   │   │   ├── Panda.fbx.meta
│   │   │   └── panda.jpg
│   │   ├── Grass/
│   │   │   ├── Fern_01.fbx
│   │   │   ├── Fern_01.fbx.meta
│   │   │   ├── Fern_02.fbx
│   │   │   ├── Fern_02.fbx.meta
│   │   │   ├── Grass_Short.fbx
│   │   │   ├── Grass_Short.fbx.meta
│   │   │   ├── Grass_Tall.fbx
│   │   │   └── Grass_Tall.fbx.meta
│   │   ├── Rocks/
│   │   │   ├── Boulder_02.fbx
│   │   │   ├── Boulder_02.fbx.meta
│   │   │   ├── MossyRock_01.fbx
│   │   │   └── MossyRock_01.fbx.meta
│   │   ├── Ruins/
│   │   │   ├── AncientColumn_01.fbx
│   │   │   ├── AncientColumn_01.fbx.meta
│   │   │   ├── BrokenPillar_01.fbx
│   │   │   └── BrokenPillar_01.fbx.meta
│   │   ├── Trees/
│   │   │   ├── Birch_01.fbx
│   │   │   ├── Birch_01.fbx.meta
│   │   │   ├── Maple_01.fbx
│   │   │   ├── Maple_01.fbx.meta
│   │   │   ├── Oak_01.fbx
│   │   │   ├── Oak_01.fbx.meta
│   │   │   ├── Oak_02.fbx
│   │   │   ├── Oak_02.fbx.meta
│   │   │   ├── TwistedTree_01.fbx
│   │   │   └── TwistedTree_01.fbx.meta
│   │   ├── Undergrowth/
│   │   │   ├── Bush_01.fbx
│   │   │   ├── Bush_01.fbx.meta
│   │   │   ├── Flowers_01.fbx
│   │   │   ├── Flowers_01.fbx.meta
│   │   │   ├── Mushrooms_01.fbx
│   │   │   └── Mushrooms_01.fbx.meta
│   │   └── Willows/
│   │       ├── WillowBurgundy.fbx
│   │       ├── WillowBurgundy.fbx.meta
│   │       ├── WillowDeepGreen_01.fbx
│   │       ├── WillowDeepGreen_01.fbx.meta
│   │       ├── Willow_Gold.fbx
│   │       └── Willow_Gold.fbx.meta
│   ├── Scenes/
│   │   ├── AmberRuinsLite.meta
│   │   ├── AmberRuinsLite/
│   │   │   ├── AmberRuinsLiteBoundaryFog.cs
│   │   │   ├── AmberRuinsLiteDetail.cs
│   │   │   ├── AmberRuinsLitePlayer.cs
│   │   │   ├── Editor/
│   │   │   │   ├── AmberRuinsLiteBuilder.cs
│   │   │   │   └── AmberRuinsLitePlayerSetup.cs
│   │   │   └── Textures/
│   │   │       └── ForestFloor_Albedo.png
│   │   ├── AmberRuinsLiteScenes.meta
│   │   ├── AmberRuinsLiteScenes/
│   │   │   └── Forest_20261011_010711_237de3a1162647a0aa9b5d033cfb6d76/
│   │   │       ├── Materials/ (26 files)
│   │   │       ├── Meshes/ (364 files)
│   │   │       ├── Scenes/
│   │   │       │   └── TheAmberRuins_Lite.unity
│   │   │       └── Textures/ (6 files)
│   │   ├── SampleScene.unity
│   │   ├── SampleScene.unity.meta
│   ├── Scripts/
│   │   └── UI/
│   │       └── StartScreenController.cs
│   ├── Settings/
│   │   ├── DefaultVolumeProfile.asset
│   │   ├── DefaultVolumeProfile.asset.meta
│   │   ├── Mobile_Renderer.asset
│   │   ├── Mobile_Renderer.asset.meta
│   │   ├── Mobile_RPAsset.asset
│   │   ├── Mobile_RPAsset.asset.meta
│   │   ├── PC_Renderer.asset
│   │   ├── PC_Renderer.asset.meta
│   │   ├── PC_RPAsset.asset
│   │   ├── PC_RPAsset.asset.meta
│   │   ├── SampleSceneProfile.asset
│   │   ├── SampleSceneProfile.asset.meta
│   │   ├── UniversalRenderPipelineGlobalSettings.asset
│   │   └── UniversalRenderPipelineGlobalSettings.asset.meta
│   ├── TutorialInfo/
│   │   ├── Layout.wlt
│   │   ├── Layout.wlt.meta
│   │   ├── Readme.cs
│   │   ├── Readme.cs.meta
│   │   ├── Editor/
│   │   │   ├── ReadmeEditor.cs
│   │   │   └── ReadmeEditor.cs.meta
│   │   ├── Icons/
│   │   │   ├── URP.png
│   │   │   └── URP.png.meta
│   │   └── StyleSheets/
│   │       ├── ReadmeEditor.uss
│   │       ├── ReadmeEditor.uss.meta
│   │       ├── ReadmeEditorDark.uss
│   │       ├── ReadmeEditorDark.uss.meta
│   │       ├── ReadmeEditorLight.uss
│   │       └── ReadmeEditorLight.uss.meta
│   ├── UI Toolkit/
│   │   └── UnityThemes/
│   │       └── UnityDefaultRuntimeTheme.tss
│   └── UI/
│       ├── Images/
│       │   └── forest-background.gif
│       ├── StartScreen.uss
│       ├── StartScreen.uxml
│       └── StartScreenPanelSettings.asset
├── Packages/
│   ├── manifest.json
│   └── packages-lock.json
├── ProjectSettings/
│   ├── AudioManager.asset
│   ├── ClusterInputManager.asset
│   ├── DynamicsManager.asset
│   ├── EditorBuildSettings.asset
│   ├── EditorSettings.asset
│   ├── GraphicsSettings.asset
│   ├── InputManager.asset
│   ├── MemorySettings.asset
│   ├── MultiplayerManager.asset
│   ├── NavMeshAreas.asset
│   ├── PackageManagerSettings.asset
│   ├── Physics2DSettings.asset
│   ├── PhysicsCoreProjectSettings2D.asset
│   ├── PresetManager.asset
│   ├── ProjectAuditorSettings.asset
│   ├── ProjectSettings.asset
│   ├── ProjectVersion.txt
│   ├── QualitySettings.asset
│   ├── SceneTemplateSettings.json
│   ├── ShaderGraphSettings.asset
│   ├── TagManager.asset
│   ├── TimeManager.asset
│   ├── UnityConnectSettings.asset
│   ├── URPProjectSettings.asset
│   ├── VersionControlSettings.asset
│   ├── VFXManager.asset
│   ├── XRSettings.asset
│   └── Packages/
│       └── com.unity.ai.assistant/
│           └── Settings.json
```
