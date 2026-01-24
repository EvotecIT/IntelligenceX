using System.Threading.Tasks;

namespace IntelligenceX.Examples;

internal interface IExample {
    string Name { get; }
    string Description { get; }
    Task RunAsync();
}
