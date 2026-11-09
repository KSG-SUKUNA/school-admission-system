using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Admission.Api.Contracts;
using Admission.Api.Data;
using Admission.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Admission.Tests;

public sealed class AdmissionApiTests
{
    private static string NewStudentId(int index) => (10000000000000000L + index).ToString();

    private static async Task<ClassResponse> CreateClassAsync(HttpClient client, int seats)
    {
        var response = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Test class " + Guid.NewGuid(), seats));
        response.EnsureSuccessStatusCode();
        using var created = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var id = created!.RootElement.GetProperty("id").GetString()!;
        return (await client.GetFromJsonAsync<ClassResponse>($"/classes/{id}"))!;
    }

    private static async Task<CreateHoldResponse> CreateHoldAsync(HttpClient client, string classId, string studentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add("X-Student-Id", studentId);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateHoldResponse>())!;
    }

    [Fact]
    public async Task CreateClass_ReturnsCreatedAndCorrectInitialSeatCounts()
    {
        using var factory = new AdmissionApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Class 6 - Morning Shift", 12));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(created);
        var id = created.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal("Class 6 - Morning Shift", created.RootElement.GetProperty("name").GetString());
        Assert.Equal(12, created.RootElement.GetProperty("totalSeats").GetInt32());

        var details = await client.GetFromJsonAsync<ClassResponse>($"/classes/{id}");
        Assert.NotNull(details);
        Assert.Equal(12, details.TotalSeats);
        Assert.Equal(0, details.HeldSeats);
        Assert.Equal(0, details.ConfirmedSeats);
        Assert.Equal(12, details.AvailableSeats);
    }

    [Fact]
    public async Task InvalidClassInput_ReturnsBadRequest()
    {
        using var factory = new AdmissionApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/classes", new CreateClassRequest(" ", 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidStudentId_ReturnsBadRequest()
    {
        using var factory = new AdmissionApiFactory();
        using var client = factory.CreateClient();
        var admissionClass = await CreateClassAsync(client, 1);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{admissionClass.Id}/holds");
        request.Headers.Add("X-Student-Id", "1234567890123456");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateActiveApplicationForSameStudentAndClass_ReturnsConflict()
    {
        using var factory = new AdmissionApiFactory();
        using var client = factory.CreateClient();
        var admissionClass = await CreateClassAsync(client, 2);
        var studentId = NewStudentId(1);

        await CreateHoldAsync(client, admissionClass.Id, studentId);
        using var duplicateRequest = new HttpRequestMessage(HttpMethod.Post, $"/classes/{admissionClass.Id}/holds");
        duplicateRequest.Headers.Add("X-Student-Id", studentId);
        var duplicateResponse = await client.SendAsync(duplicateRequest);

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        var updated = await client.GetFromJsonAsync<ClassResponse>($"/classes/{admissionClass.Id}");
        Assert.NotNull(updated);
        Assert.Equal(1, updated.HeldSeats);
        Assert.Equal(1, updated.AvailableSeats);
    }

    [Fact]
    public async Task ConfirmingTheSameHoldTwice_IncrementsConfirmedSeatsOnlyOnce()
    {
        using var factory = new AdmissionApiFactory();
        using var client = factory.CreateClient();
        var admissionClass = await CreateClassAsync(client, 2);
        var studentId = NewStudentId(2);
        var hold = await CreateHoldAsync(client, admissionClass.Id, studentId);

        for (var i = 0; i < 2; i++)
        {
            using var confirmRequest = new HttpRequestMessage(HttpMethod.Post, $"/holds/{hold.HoldId}/confirm");
            confirmRequest.Headers.Add("X-Student-Id", studentId);
            var confirmResponse = await client.SendAsync(confirmRequest);
            Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        }

        var updated = await client.GetFromJsonAsync<ClassResponse>($"/classes/{admissionClass.Id}");
        Assert.NotNull(updated);
        Assert.Equal(1, updated.ConfirmedSeats);
        Assert.Equal(1, updated.AvailableSeats);
    }

    [Fact]
    public async Task ExpiredHoldIsReportedAsExpiredAndSeatIsReleased()
    {
        using var factory = new AdmissionApiFactory();
        using var client = factory.CreateClient();
        var admissionClass = await CreateClassAsync(client, 1);
        var studentId = NewStudentId(3);
        var hold = await CreateHoldAsync(client, admissionClass.Id, studentId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
            var entity = await db.AdmissionHolds.SingleAsync(x => x.Id == hold.HoldId);
            entity.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var status = await client.GetFromJsonAsync<HoldResponse>($"/holds/{hold.HoldId}");
        var updatedClass = await client.GetFromJsonAsync<ClassResponse>($"/classes/{admissionClass.Id}");
        Assert.NotNull(status);
        Assert.Equal("EXPIRED", status.Status);
        Assert.NotNull(updatedClass);
        Assert.Equal(1, updatedClass.AvailableSeats);
    }

    [Fact]
    public async Task ExpiredHoldCannotBeConfirmed()
    {
        using var factory = new AdmissionApiFactory();
        using var client = factory.CreateClient();
        var admissionClass = await CreateClassAsync(client, 1);
        var studentId = NewStudentId(4);
        var hold = await CreateHoldAsync(client, admissionClass.Id, studentId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
            var entity = await db.AdmissionHolds.SingleAsync(x => x.Id == hold.HoldId);
            entity.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var confirmRequest = new HttpRequestMessage(HttpMethod.Post, $"/holds/{hold.HoldId}/confirm");
        confirmRequest.Headers.Add("X-Student-Id", studentId);
        var response = await client.SendAsync(confirmRequest);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var updatedClass = await client.GetFromJsonAsync<ClassResponse>($"/classes/{admissionClass.Id}");
        Assert.NotNull(updatedClass);
        Assert.Equal(1, updatedClass.AvailableSeats);
    }

    [Fact]
    public async Task CancellingHeldApplicationReleasesSeat()
    {
        using var factory = new AdmissionApiFactory();
        using var client = factory.CreateClient();
        var admissionClass = await CreateClassAsync(client, 1);
        var studentId = NewStudentId(5);
        var hold = await CreateHoldAsync(client, admissionClass.Id, studentId);

        using var cancelRequest = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{hold.HoldId}");
        cancelRequest.Headers.Add("X-Student-Id", studentId);
        var cancelResponse = await client.SendAsync(cancelRequest);

        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);
        var updatedClass = await client.GetFromJsonAsync<ClassResponse>($"/classes/{admissionClass.Id}");
        Assert.NotNull(updatedClass);
        Assert.Equal(0, updatedClass.HeldSeats);
        Assert.Equal(1, updatedClass.AvailableSeats);

        using var myHoldsRequest = new HttpRequestMessage(HttpMethod.Get, "/me/holds");
        myHoldsRequest.Headers.Add("X-Student-Id", studentId);
        var myHoldsResponse = await client.SendAsync(myHoldsRequest);
        var myHolds = await myHoldsResponse.Content.ReadFromJsonAsync<MyHoldResponse[]>();
        Assert.NotNull(myHolds);
        Assert.Contains(myHolds, item => item.Status == "CANCELLED");
    }

    [Fact]
    public async Task ConfirmedAdmissionCannotBeCancelled()
    {
        using var factory = new AdmissionApiFactory();
        using var client = factory.CreateClient();
        var admissionClass = await CreateClassAsync(client, 1);
        var studentId = NewStudentId(6);
        var hold = await CreateHoldAsync(client, admissionClass.Id, studentId);

        using var confirmRequest = new HttpRequestMessage(HttpMethod.Post, $"/holds/{hold.HoldId}/confirm");
        confirmRequest.Headers.Add("X-Student-Id", studentId);
        var confirmResponse = await client.SendAsync(confirmRequest);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        using var cancelRequest = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{hold.HoldId}");
        cancelRequest.Headers.Add("X-Student-Id", studentId);
        var cancelResponse = await client.SendAsync(cancelRequest);

        Assert.Equal(HttpStatusCode.Conflict, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRequestsNeverHoldMoreSeatsThanCapacity()
    {
        using var factory = new AdmissionApiFactory();
        using var client = factory.CreateClient();
        const int capacity = 5;
        const int requestCount = 30;
        var admissionClass = await CreateClassAsync(client, capacity);

        var requests = Enumerable.Range(1, requestCount).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{admissionClass.Id}/holds");
            request.Headers.Add("X-Student-Id", NewStudentId(index + 100));
            return await client.SendAsync(request);
        });

        var responses = await Task.WhenAll(requests);
        var created = responses.Count(x => x.StatusCode == HttpStatusCode.Created);
        var conflicts = responses.Count(x => x.StatusCode == HttpStatusCode.Conflict);
        var finalClass = await client.GetFromJsonAsync<ClassResponse>($"/classes/{admissionClass.Id}");

        Assert.Equal(capacity, created);
        Assert.Equal(requestCount - capacity, conflicts);
        Assert.NotNull(finalClass);
        Assert.True(finalClass.HeldSeats + finalClass.ConfirmedSeats <= finalClass.TotalSeats);
    }

    [Fact]
    public async Task TwoIndependentApiHostsSharingDatabaseRespectTheSameSeatLimit()
    {
        using var firstFactory = new AdmissionApiFactory();
        using var secondFactory = new AdmissionApiFactory();
        using var firstClient = firstFactory.CreateClient();
        using var secondClient = secondFactory.CreateClient();
        const int capacity = 3;
        var admissionClass = await CreateClassAsync(firstClient, capacity);

        var requests = Enumerable.Range(1, 20).Select(async index =>
        {
            var client = index % 2 == 0 ? firstClient : secondClient;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{admissionClass.Id}/holds");
            request.Headers.Add("X-Student-Id", NewStudentId(index + 500));
            return await client.SendAsync(request);
        });

        var responses = await Task.WhenAll(requests);
        Assert.Equal(capacity, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        var finalClass = await firstClient.GetFromJsonAsync<ClassResponse>($"/classes/{admissionClass.Id}");
        Assert.NotNull(finalClass);
        Assert.True(finalClass.HeldSeats + finalClass.ConfirmedSeats <= finalClass.TotalSeats);
    }
}
