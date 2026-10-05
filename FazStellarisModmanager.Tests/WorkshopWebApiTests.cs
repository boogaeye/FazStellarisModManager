using System.Net;
using FazStellarisModmanager.Core.Workshop;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class WorkshopWebApiTests
{
    const string Ok = """
        {"response":{"result":1,"resultcount":3,"publishedfiledetails":[
          {"publishedfileid":"1419304439","result":1,"title":"Ancient Cache of Technologies","file_size":"5242880"},
          {"publishedfileid":"42","result":9},
          {"publishedfileid":"7","result":1,"title":"Seven","file_size":1234}
        ]}}
        """;

    [Fact]
    public void Parses_titles_and_sizes_given_as_strings_or_numbers()
    {
        var items = WorkshopWebApi.ParseDetails(Ok);

        Assert.Equal(
            [new WorkshopItemInfo(1419304439, "Ancient Cache of Technologies", 5242880), new WorkshopItemInfo(42, null, null), new WorkshopItemInfo(7, "Seven", 1234)],
            items);
    }

    [Theory]
    [InlineData("""{"publishedfileid":"5","result":9,"title":"Hidden","file_size":"10"}""")]
    [InlineData("""{"publishedfileid":"5","result":1,"file_size":"10"}""")]
    [InlineData("""{"publishedfileid":"5","result":1,"title":"","file_size":10}""")]
    public void A_failed_result_or_a_missing_title_is_unknown(string entry)
    {
        var item = Assert.Single(WorkshopWebApi.ParseDetails("""{"response":{"publishedfiledetails":[""" + entry + "]}}"));

        Assert.Equal(new WorkshopItemInfo(5, null, null), item);
    }

    [Fact]
    public void A_missing_or_unreadable_size_is_unknown_but_the_title_is_kept()
    {
        var items = WorkshopWebApi.ParseDetails("""
            {"response":{"publishedfiledetails":[
              {"publishedfileid":"1","result":1,"title":"A"},
              {"publishedfileid":"2","result":1,"title":"B","file_size":"lots"}
            ]}}
            """);

        Assert.Equal([new WorkshopItemInfo(1, "A", null), new WorkshopItemInfo(2, "B", null)], items);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"response\":")]
    [InlineData("[]")]
    [InlineData("{\"response\":{\"publishedfiledetails\":5}}")]
    public void Malformed_documents_throw_unavailable(string json) =>
        Assert.Throws<WorkshopUnavailableException>(() => WorkshopWebApi.ParseDetails(json));

    [Fact]
    public async Task Posts_the_ids_form_encoded_and_returns_one_entry_per_id_in_order()
    {
        string? body = null;
        var handler = FakeHttp.Async(async r =>
        {
            body = await r.Content!.ReadAsStringAsync();
            return FakeHttp.Json(Ok);
        });

        var items = await WorkshopWebApi.GetDetailsAsync(new HttpClient(handler), [7, 1419304439, 99], CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", request.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("itemcount=3&publishedfileids[0]=7&publishedfileids[1]=1419304439&publishedfileids[2]=99", WebUtility.UrlDecode(body));
        Assert.Equal(
            [new WorkshopItemInfo(7, "Seven", 1234), new WorkshopItemInfo(1419304439, "Ancient Cache of Technologies", 5242880), new WorkshopItemInfo(99, null, null)],
            items);
    }

    [Fact]
    public async Task No_ids_needs_no_request()
    {
        var handler = new FakeHttp(_ => throw new InvalidOperationException("no request expected"));

        Assert.Empty(await WorkshopWebApi.GetDetailsAsync(new HttpClient(handler), [], CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Network_failures_and_error_statuses_throw_unavailable()
    {
        var down = new FakeHttp(_ => throw new HttpRequestException("No such host."));
        var error = new FakeHttp(_ => FakeHttp.Json("{}", HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<WorkshopUnavailableException>(() => WorkshopWebApi.GetDetailsAsync(new HttpClient(down), [1], CancellationToken.None));
        await Assert.ThrowsAsync<WorkshopUnavailableException>(() => WorkshopWebApi.GetDetailsAsync(new HttpClient(error), [1], CancellationToken.None));
    }

    [Fact]
    public async Task A_timeout_throws_unavailable_but_a_cancel_is_a_cancel()
    {
        var hang = new Hang();
        // HttpClient.Timeout stands in for the 15 s limit (both surface as a TaskCanceledException not caused by ct).
        var slow = new HttpClient(hang) { Timeout = TimeSpan.FromMilliseconds(100) };

        await Assert.ThrowsAsync<WorkshopUnavailableException>(() => WorkshopWebApi.GetDetailsAsync(slow, [1], CancellationToken.None));

        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkshopWebApi.GetDetailsAsync(new HttpClient(hang), [1], cts.Token));
    }

    sealed class Hang : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
