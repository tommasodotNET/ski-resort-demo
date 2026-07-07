using Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Routing helpers for local development of Foundry-hosted agents.
/// </summary>
public static class HostedContributorRouteExtensions
{
    /// <summary>
    /// In Development, maps the per-agent OpenAI route shape that live Foundry uses.
    /// </summary>
    public static WebApplication MapDevTemporaryLocalAgentEndpoint(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Environment.IsDevelopment())
        {
            app.MapFoundryResponses("api/projects/{project}/agents/{agentName}/endpoint/protocols/openai");
        }

        return app;
    }
}
