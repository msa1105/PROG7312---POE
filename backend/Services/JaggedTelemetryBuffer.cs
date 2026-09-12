namespace SmartX.Api.Services;

using SmartX.Api.Models;

public class JaggedTelemetryBuffer
{
    // 4 parallel ingestion channels with dynamic packet batch sizes
    private readonly double[][] _rawChannels = new double[4][];

    public void LoadChannelBatch(int channelIndex, double[] readings)
    {
        if (channelIndex >= 0 && channelIndex < _rawChannels.Length)
            _rawChannels[channelIndex] = readings;
    }

    public List<TelemetryPacket<double>> FlushToList(string macAddress, string unit)
    {
        var result = new List<TelemetryPacket<double>>();
        for (int i = 0; i < _rawChannels.Length; i++)
        {
            if (_rawChannels[i] == null) continue;
            for (int j = 0; j < _rawChannels[i].Length; j++)
            {
                result.Add(new TelemetryPacket<double>
                {
                    MacAddress = macAddress,
                    Value = _rawChannels[i][j],
                    MetricUnit = unit
                });
            }
        }
        return result;
    }
}
