# RayCi Single-Camera Compatibility Bridge

This workspace is now focused on one camera only:

- Daheng model prefix `MER-130-30UM`
- Any serial number, with optional `BEAMMIC_DAHENG_SN` override
- `Mono10`
- `1280 x 1024`

The outward identity exposed to RayCi stays fixed as:

- `CinCam CMOS 1201 EL`
- serial `1201EL-U2-1022-0034`

That fixed shell is only for RayCi compatibility. The image stream, exposure, gain, and register-page emulation are all driven by the real Daheng camera.

## Main components

- `virtual_ueye_proxy`
  - exports the `ueye_api_64.dll` compatibility layer expected by RayCi
  - exposes a single fixed RayCi-facing camera identity
- `daheng_frame_server`
  - opens only Daheng USB cameras whose model starts with `MER-130-30UM`
  - prefers `Mono10`
  - publishes `1280x1024` frames to the bridge
  - no longer auto-falls back to synthetic white-noise mode unless explicitly forced
- `seed-rayci-calibration-registry.ps1`
  - seeds only the minimal RayCi 2022 calibration keys needed for this one camera shell
- `prepare-rayci-hybrid-portable.ps1`
  - builds a portable RayCi folder with the uEye proxy and Daheng helper
  - preserves the original `FGCamera.dll` from the RayCi install instead of replacing it

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build-rayci-ueye-bridge.ps1
```

Artifacts are published to:

- `D:\work\ultron\rayci-10bit\artifacts\ueye_proxy`
- `D:\work\ultron\rayci-10bit\artifacts\DahengBridgeHelper`

## Prepare portable RayCi

```powershell
powershell -ExecutionPolicy Bypass -File .\prepare-rayci-hybrid-portable.ps1
```

Default output:

- `D:\work\ultron\rayci-10bit\dist\RayCi64Lite-HybridBridge-final`

## Launch and verify

```powershell
powershell -ExecutionPolicy Bypass -File .\launch-rayci-ueye-white-noise.ps1 -CloseExisting -FinalizeOpenLiveMode -Verify
```

Despite the legacy filename, the launcher now runs only the real-camera path. It clears old simulation and compatibility environment variables, seeds the single-camera registry shell, and starts RayCi against:

- model prefix `MER-130-30UM`
- pixel format `Mono10`
- frame size `1280x1024`

Verification dumps are written beside the workspace using the selected dump prefix.

## Logs

- `%LOCALAPPDATA%\Ultron\RayCiUeyeBridge\logs\ueye_proxy.log`
- `%LOCALAPPDATA%\Ultron\RayCiUeyeBridge\logs\daheng_frame_server.log`
- `%LOCALAPPDATA%\Ultron\RayCiUeyeBridge\logs\bridge_identity_report.txt`

## Runtime intent

The project is no longer intended to be a broad simulator or a multi-camera bridge. The maintained path is:

1. RayCi loads `ueye_api_64.dll`.
2. The proxy reports one fixed compatible camera shell.
3. The Daheng helper opens the real Daheng camera whose model starts with `MER-130-30UM`.
4. Real `Mono10` frames are converted into the `Y16` container RayCi expects.
5. RayCi exposure and gain calls are translated back into Daheng feature control.
