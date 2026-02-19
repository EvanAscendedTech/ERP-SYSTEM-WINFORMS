Place offline converter binaries in this folder:
- step2glb.exe
- any dependent DLLs required by the converter

Build behavior:
- ERPSystem.WinForms build validates that Tools\step2glb\step2glb.exe exists.
- If Tools\step2glb\src\step2glb.csproj exists and step2glb.exe is missing,
  the build runs `dotnet publish` for that project into this folder.
