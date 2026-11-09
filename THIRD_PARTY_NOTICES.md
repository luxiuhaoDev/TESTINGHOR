# Third-party notices / 第三方声明

McPetOverlay 的主体实现为 Windows/.NET 外部覆盖程序。下列项目是已确认的设计或表现参考；本仓库不声称拥有它们的原始代码、名称或美术。

## NekoyaHouse/Epsilon

- Repository: https://github.com/NekoyaHouse/Epsilon
- License: GNU General Public License v3.0
- Referenced aspects: dark visual hierarchy, category/module/settings interaction, rounded panels, transition timing and modular UI presentation.
- McPetOverlay implementation: the WinForms/GDI+ controls in `UI/Controls/`, `UI/Forms/EpsilonWorkbenchCoordinator.cs` and `UI/Theme/EpsilonTheme.cs` are the local C# implementation and adaptation.

## Myau / Poorcatgirl visual lineage

- Local historical reference name: `Myau-Poorcatgirl-Fresh`
- License recorded in the original project notice: GNU General Public License v3.0
- Referenced aspects: ESP/HUD presentation conventions, such as target cards, equipment presentation and compact visual overlays.
- Public source status: the exact repository URL used during the original development has not yet been recovered. A related public GPLv3 codebase is OpenMyau: https://github.com/60124808866/OpenMyau . It is listed as related context, not claimed to be the exact missing source.

## Microsoft / platform APIs

- .NET 8 and Windows Forms are used under their respective Microsoft licenses.
- Win32, DWM, GDI/GDI+ and HotSpot serviceability symbols are platform interfaces; their use does not transfer ownership of Microsoft, Oracle, OpenJDK, Mojang or Minecraft trademarks.

## Compliance requirements

- Retain `LICENSE`, this notice and `AUTHORS.md` in redistributed source or binaries.
- Mark modifications clearly.
- Provide corresponding source when distributing GPLv3-covered binaries.
- Do not present third-party projects as authored by `luxiuhaoDev`, and do not present McPetOverlay's original implementation as authored by those third parties.
