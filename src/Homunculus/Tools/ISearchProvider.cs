namespace Homunculus.Tools;

/// <summary>A web search engine the web_search tool asks.</summary>
public interface ISearchProvider
{
    /// <exception cref="ToolException">The search could not be done.</exception>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int count, CancellationToken cancellation);
}
