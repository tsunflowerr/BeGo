using OptiGo.Application.UseCases;

namespace OptiGo.Application.Interfaces;

public interface ITrafficSnapshotProvider
{
    TrafficSnapshot GetCurrentSnapshot();
}
