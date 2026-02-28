namespace ParticularLLM.Viewer.Scenarios;

public record ScenarioDef(
    string Name,
    string Category,
    string Description,
    Action<ViewerFixture> Setup,
    string[] Tags,
    int Width = 64,
    int Height = 64,
    int SuggestedFrames = 300
);

public interface IScenarioProvider
{
    IEnumerable<ScenarioDef> GetScenarios();
}
