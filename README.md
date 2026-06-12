# RayCi USB camera bridge bring-up

Correction on 2026-06-10:

- `MER-130-30UM-L` and `RH1015005021` may appear in older notes, tools, or raw page captures.
- They should now be treated as suspect raw-page identity fields, not as the default outward identity of the simulator.

This machine can see the Daheng camera and `GalaxyView` can display the image stream, but the default SDK install is mixed with an older system DLL:

- `C:\Windows\System32\GxIAPI.dll` -> `1.10.2105.8281`
- `C:\Program Files\Daheng Imaging\GalaxySDK\APIDll\Win64\GxIAPI.dll` -> `2.0.2603.8121`

That mismatch causes the stock SDK tools and samples to fail with entry-point / ordinal errors when they pick up the old `System32` DLL first.

## Quick start

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\run-galaxy-viewer.ps1
```

The script builds a local `GalaxyViewPortable` folder in this workspace by combining:

- `GalaxySDK\Demo\Win64`
- `GalaxySDK\APIDll\Win64`

Then it launches `GalaxyView.exe` from that folder so the matching camera DLLs are loaded first.

## Probe streaming

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\run-daheng-probe.ps1
```

The probe does not depend on `GalaxyView`. It opens the first detected Daheng camera and tests both:

- `TriggerMode=Off` free-run
- `TriggerMode=On` with `TriggerSource=Software`

It prints whether frames are actually received from the stream.

## GenICam / GenTL compatibility

This machine already has GenTL producers installed and exposed through `GENICAM_GENTL64_PATH`:

- `C:\Program Files\Daheng Imaging\GalaxySDK\GenTL\Win64`
- `C:\Program Files\CINOGY\Driver\CMOS_EL_USB\GenICam\TL`

For this camera, the relevant producer is:

- `GxUSBTL.cti`

Do not confuse it with:

- `GxU3VTL.cti` for USB3 Vision cameras
- `GxGVTL.cti` for GigE Vision cameras

If a third-party GenTL consumer needs a cleaner environment, launch it with:

```powershell
powershell -ExecutionPolicy Bypass -File .\launch-with-daheng-gentl.ps1 -Program "C:\Path\To\YourConsumer.exe"
```

Add `-IncludeCinogy` if the OEM CTI is the one your application expects.

## Inspect device state

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\inspect-daheng-camera.ps1
```

This prints:

- the detected Daheng / USB Vision camera devices
- the `GxIAPI.dll` versions from the SDK and from `System32`

## Notes

- The workspace copy is safer than replacing `C:\Windows\System32\GxIAPI.dll`.
- Some host tools previously showed `MER-130-30UM-L`, but that name is now treated as a suspect raw-page identity field for simulation purposes.
- If the SDK gets upgraded, rerun `run-galaxy-viewer.ps1 -ForceRefresh`.
- On this machine the camera driver currently reports `DEVPKEY_Device_IsRebootRequired=True`, and `pnputil /restart-device` refuses to restart it until the system reboots.
- Current probe result before reboot: the camera opens successfully, but both free-run and software-trigger acquisition time out with zero frames received.

## After reboot findings

- After the real Windows restart on `2026-06-09 01:04:24`, `DEVPKEY_Device_IsRebootRequired` changed to `False`.
- Even after reboot, the probe still reports:

```text
SuccessfulFrames FreeRun=0 SoftTrigger=0
```

- `Bus Hound` capture is saved as [bushound_capture.txt](/D:/work/ultron/rayci-10bit/bushound_capture.txt).
- The capture shows the Daheng camera responding on control endpoint `10.0` with many vendor control transfers.
- The capture does **not** show any image payload transfers on stream endpoints. The only non-control endpoint activity seen for the camera is repeated `10.2 RESET`.

This means the control plane is alive, but the image stream endpoint never starts delivering frame data.

## Current best hypotheses

1. USB2 camera streaming is not compatible with the current host path on this machine, most likely the Windows 11 + Intel xHCI path.
2. The camera hardware/firmware can answer control commands, but its streaming endpoint is failing when acquisition starts.

## Highest-value next checks

1. Try the camera through a true USB 2.0 hub or a native USB 2.0 port.
2. Try a different USB cable, ideally a short USB-IF certified cable with screw lock.
3. Try the same camera on another PC, preferably Windows 10.

Interpretation:

- If it works on a Windows 10 machine or behind a USB 2.0 hub, the problem is host compatibility on this PC.
- If it still has zero frames on another machine, the camera itself is very likely faulty at the streaming side.

## RayCi uEye bridge

This workspace now includes a `uEye`-compatible bridge that lets `RayCi` load a Daheng-backed virtual camera through a local `ueye_api_64.dll`.

Build the bridge artifacts with:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-rayci-ueye-bridge.ps1
```

Prepare a portable `RayCi` folder without modifying `C:\Program Files`:

```powershell
powershell -ExecutionPolicy Bypass -File .\prepare-rayci-bridge-portable.ps1
```

The portable output is created under:

- `D:\work\ultron\rayci-10bit\dist\RayCi64Lite-UeyeBridge`

Current stable hybrid package with working image display:

- `D:\work\ultron\rayci-10bit\dist\RayCi64Lite-HybridBridge-final`
- Verified state: `10bpp (Y16): 1280 x 1024 at ~14 fps`
- Verification dump: `D:\work\ultron\rayci-10bit\result_live_verify.control.txt`

Bridge behavior:

- `ueye_api_64.dll` is copied into the portable `RayCi` folder.
- `DahengFrameServer.exe` and its Daheng runtime files are copied into `DahengBridgeHelper`.
- The proxy first looks for the helper in `DahengBridgeHelper`, then beside `RayCi.exe`.
- You can override helper discovery with the environment variable `ULTRON_RAYCI_BRIDGE_HELPER`.

Bridge logs are written to:

- `%LOCALAPPDATA%\Ultron\RayCiUeyeBridge\logs`

## Synthetic white-noise feed

The bridge simulation can now feed repeatable white-noise frames into `RayCi` without a physical camera.

Use:

```powershell
$env:ULTRON_RAYCI_SIMULATE = '1'
$env:ULTRON_RAYCI_AUTO_SIMULATE = '1'
$env:ULTRON_RAYCI_SIM_PATTERN = 'white-noise'
```

Then launch the portable `RayCi` bridge build. The default synthetic mode remains the existing moving beam/target pattern, so omit `ULTRON_RAYCI_SIM_PATTERN` if you want the earlier behavior.

For the ready-to-run shortcut, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\launch-rayci-ueye-white-noise.ps1
```

The launch script now defaults to the stable hybrid package above.
