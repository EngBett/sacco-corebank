using Wolverine;

namespace Sacco.Seed;

/// <summary>The seeder never runs sagas; the Payments module only needs a bus registered to construct its services.</summary>
public sealed class SeedMessageBusStub : IMessageBus
{
    private static NotSupportedException NotInSeed() => new("Messaging is not available during seeding.");
    public string? TenantId { get; set; }
    public IDestinationEndpoint EndpointFor(string endpointName) => throw NotInSeed();
    public IDestinationEndpoint EndpointFor(Uri uri) => throw NotInSeed();
    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw NotInSeed();
    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw NotInSeed();
    public Task<T> InvokeAsync<T>(object message, DeliveryOptions options, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw NotInSeed();
    public Task InvokeAsync(object message, DeliveryOptions options, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw NotInSeed();
    public IAsyncEnumerable<T> StreamAsync<T>(object message, CancellationToken cancellation = default) => throw NotInSeed();
    public IAsyncEnumerable<T> StreamAsync<T>(object message, DeliveryOptions options, CancellationToken cancellation = default) => throw NotInSeed();
    public Task<TOut> StreamAsync<TIn, TOut>(IAsyncEnumerable<TIn> messages, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw NotInSeed();
    public Task<TOut> StreamAsync<TIn, TOut>(IAsyncEnumerable<TIn> messages, DeliveryOptions options, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw NotInSeed();
    public Task<T> InvokeForTenantAsync<T>(string tenantId, object message, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw NotInSeed();
    public Task InvokeForTenantAsync(string tenantId, object message, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw NotInSeed();
    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => [];
    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) => [];
    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw NotInSeed();
    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) => throw NotInSeed();
    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) => throw NotInSeed();
    public Guid CorrelationId { get; set; }
}
