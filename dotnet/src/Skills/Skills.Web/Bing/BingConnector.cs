﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.Diagnostics;

namespace Microsoft.SemanticKernel.Skills.Web.Bing;

/// <summary>
/// Bing API connector.
/// </summary>
public sealed class BingConnector : IWebSearchEngineConnector
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="BingConnector"/> class.
    /// </summary>
    /// <param name="apiKey">The API key to authenticate the connector.</param>
    /// <param name="logger">An optional logger to log connector-related information.</param>
    public BingConnector(string apiKey, ILogger<BingConnector>? logger = null) :
        this(apiKey, new HttpClient(NonDisposableHttpClientHandler.Instance, false), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BingConnector"/> class.
    /// </summary>
    /// <param name="apiKey">The API key to authenticate the connector.</param>
    /// <param name="httpClient">The HTTP client to use for making requests.</param>
    /// <param name="logger">An optional logger to log connector-related information.</param>
    public BingConnector(string apiKey, HttpClient httpClient, ILogger<BingConnector>? logger = null)
    {
        Verify.NotNull(httpClient);

        this._apiKey = apiKey;
        this._logger = logger ?? NullLogger<BingConnector>.Instance;
        this._httpClient = httpClient;
        this._httpClient.DefaultRequestHeaders.Add("User-Agent", Telemetry.HttpUserAgent);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<string>> SearchAsync(string query, int count = 1, int offset = 0, CancellationToken cancellationToken = default)
    {
        if (count <= 0) { throw new ArgumentOutOfRangeException(nameof(count)); }

        if (count >= 50) { throw new ArgumentOutOfRangeException(nameof(count), $"{nameof(count)} value must be less than 50."); }

        if (offset < 0) { throw new ArgumentOutOfRangeException(nameof(offset)); }

        Uri uri = new($"https://api.bing.microsoft.com/v7.0/search?q={Uri.EscapeDataString(query)}&count={count}&offset={offset}");

        this._logger.LogDebug("Sending request: {0}", uri);

        using HttpResponseMessage response = await this.SendGetRequest(uri, cancellationToken).ConfigureAwait(false);

        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        this._logger.LogDebug("Response received: {0}", response.StatusCode);

        this._logger.LogTrace("Response content received: {0}", json);

        response.EnsureSuccess(json, this._logger);

        BingSearchResponse? data = JsonSerializer.Deserialize<BingSearchResponse>(json);

        WebPage[]? results = data?.WebPages?.Value;

        return results == null ? Enumerable.Empty<string>() : results.Select(x => x.Snippet);
    }

    /// <summary>
    /// Sends a GET request to the specified URI.
    /// </summary>
    /// <param name="uri">The URI to send the request to.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the request.</param>
    /// <returns>A <see cref="HttpResponseMessage"/> representing the response from the request.</returns>
    private async Task<HttpResponseMessage> SendGetRequest(Uri uri, CancellationToken cancellationToken = default)
    {
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

        if (!string.IsNullOrEmpty(this._apiKey))
        {
            httpRequestMessage.Headers.Add("Ocp-Apim-Subscription-Key", this._apiKey);
        }

        return await this._httpClient.SendAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage("Performance", "CA1812:Internal class that is apparently never instantiated",
        Justification = "Class is instantiated through deserialization.")]
    private sealed class BingSearchResponse
    {
        [JsonPropertyName("webPages")]
        public WebPages? WebPages { get; set; }
    }

    [SuppressMessage("Performance", "CA1812:Internal class that is apparently never instantiated",
        Justification = "Class is instantiated through deserialization.")]
    private sealed class WebPages
    {
        [JsonPropertyName("value")]
        public WebPage[]? Value { get; set; }
    }

    [SuppressMessage("Performance", "CA1812:Internal class that is apparently never instantiated",
        Justification = "Class is instantiated through deserialization.")]
    private sealed class WebPage
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("snippet")]
        public string Snippet { get; set; } = string.Empty;
    }
}
