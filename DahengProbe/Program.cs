using GxIAPINET;

static class Program
{
    static int Main()
    {
        IGXDevice? device = null;
        IGXStream? stream = null;

        try
        {
            IGXFactory.GetInstance().Init();

            var devices = new List<IGXDeviceInfo>();
            IGXFactory.GetInstance().UpdateAllDeviceList(1000, devices);
            Console.WriteLine($"Detected devices: {devices.Count}");
            if (devices.Count < 1)
            {
                Console.WriteLine("No Daheng camera found.");
                return 2;
            }

            var info = devices[0];
            Console.WriteLine($"Using SN={info.GetSN()}");
            Console.WriteLine($"Vendor={info.GetVendorName()}");
            Console.WriteLine($"Model={info.GetModelName()}");
            Console.WriteLine($"DisplayName={info.GetDisplayName()}");

            device = IGXFactory.GetInstance().OpenDeviceBySN(info.GetSN(), GX_ACCESS_MODE.GX_ACCESS_CONTROL);
            var remote = device.GetRemoteFeatureControl();

            DumpEnum(remote, "AcquisitionMode");
            DumpEnum(remote, "TriggerMode");
            DumpEnum(remote, "TriggerSource");
            DumpEnum(remote, "TriggerSelector");
            DumpFloat(remote, "ExposureTime");
            DumpFloat(remote, "Gain");

            TrySetEnum(remote, "AcquisitionMode", "Continuous");

            stream = device.OpenStream(0);
            var streamFeatures = stream.GetFeatureControl();
            TrySetEnum(streamFeatures, "StreamBufferHandlingMode", "OldestFirst");

            var freeRunConfigured = TrySetEnum(remote, "TriggerMode", "Off");
            Console.WriteLine($"FreeRunConfigured={freeRunConfigured}");
            DumpEnum(remote, "TriggerMode");
            DumpEnum(remote, "TriggerSource");
            var freeRunFrames = CaptureFrames(stream, remote, useSoftwareTrigger: false, label: "FreeRun");

            var softwareTriggerConfigured = TrySetEnum(remote, "TriggerMode", "On");
            softwareTriggerConfigured &= TrySetEnum(remote, "TriggerSource", "Software");
            Console.WriteLine($"SoftwareTriggerConfigured={softwareTriggerConfigured}");
            DumpEnum(remote, "TriggerMode");
            DumpEnum(remote, "TriggerSource");
            var softwareFrames = softwareTriggerConfigured
                ? CaptureFrames(stream, remote, useSoftwareTrigger: true, label: "SoftTrigger")
                : 0;

            Console.WriteLine($"SuccessfulFrames FreeRun={freeRunFrames} SoftTrigger={softwareFrames}");
            return (freeRunFrames > 0 || softwareFrames > 0) ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal: {ex}");
            return 1;
        }
        finally
        {
            try
            {
                stream?.Close();
            }
            catch
            {
            }

            try
            {
                device?.Close();
            }
            catch
            {
            }

            try
            {
                IGXFactory.GetInstance().Uninit();
            }
            catch
            {
            }
        }
    }

    static void DumpEnum(IGXFeatureControl featureControl, string name)
    {
        try
        {
            if (!featureControl.IsImplemented(name))
            {
                Console.WriteLine($"{name}=<not implemented>");
                return;
            }

            if (!featureControl.IsReadable(name))
            {
                Console.WriteLine($"{name}=<not readable>");
                return;
            }

            Console.WriteLine($"{name}={featureControl.GetEnumFeature(name).GetValue()}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{name}=<error: {ex.Message}>");
        }
    }

    static void DumpFloat(IGXFeatureControl featureControl, string name)
    {
        try
        {
            if (!featureControl.IsImplemented(name))
            {
                Console.WriteLine($"{name}=<not implemented>");
                return;
            }

            if (!featureControl.IsReadable(name))
            {
                Console.WriteLine($"{name}=<not readable>");
                return;
            }

            var feature = featureControl.GetFloatFeature(name);
            Console.WriteLine($"{name}={feature.GetValue()} {feature.GetUnit()}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{name}=<error: {ex.Message}>");
        }
    }

    static bool TrySetEnum(IGXFeatureControl featureControl, string name, string value)
    {
        try
        {
            if (!featureControl.IsImplemented(name))
            {
                Console.WriteLine($"Set {name}={value} skipped: feature not implemented.");
                return false;
            }

            if (!featureControl.IsWritable(name))
            {
                Console.WriteLine($"Set {name}={value} skipped: feature not writable.");
                return false;
            }

            featureControl.GetEnumFeature(name).SetValue(value);
            Console.WriteLine($"Set {name}={value} ok.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Set {name}={value} failed: {ex.Message}");
            return false;
        }
    }

    static int CaptureFrames(IGXStream stream, IGXFeatureControl remote, bool useSoftwareTrigger, string label)
    {
        var okFrames = 0;

        try
        {
            stream.StartGrab();
            remote.GetCommandFeature("AcquisitionStart").Execute();

            for (var i = 0; i < 5; i++)
            {
                if (useSoftwareTrigger)
                {
                    try
                    {
                        stream.FlushQueue();
                    }
                    catch
                    {
                    }

                    remote.GetCommandFeature("TriggerSoftware").Execute();
                }

                try
                {
                    var frame = stream.DQBuf(1200);
                    try
                    {
                        Console.WriteLine(
                            $"{label}[{i}] status={frame.GetStatus()} width={frame.GetWidth()} height={frame.GetHeight()} frameId={frame.GetFrameID()}");
                        if (frame.GetStatus() == GX_FRAME_STATUS_LIST.GX_FRAME_STATUS_SUCCESS)
                        {
                            okFrames++;
                        }
                    }
                    finally
                    {
                        stream.QBuf(frame);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{label}[{i}] exception: {ex.Message}");
                }
            }
        }
        finally
        {
            try
            {
                remote.GetCommandFeature("AcquisitionStop").Execute();
            }
            catch
            {
            }

            try
            {
                stream.StopGrab();
            }
            catch
            {
            }
        }

        return okFrames;
    }
}
