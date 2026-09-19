using MongoDB.Driver;
using MockedMpesa.API.Models;

namespace MockedMpesa.API.Data;

/// <summary>
/// MongoDB database context for Mock M-Pesa service
/// </summary>
public class MockMpesaDbContext
{
    private readonly IMongoDatabase _database;

    public MockMpesaDbContext(IMongoDatabase database)
    {
        _database = database;
        
        // Create indexes for query performance
        CreateIndexes();
    }

    public IMongoCollection<TransactionRecord> Transactions => 
        _database.GetCollection<TransactionRecord>("Transactions");
    
    public IMongoCollection<SimulationSettings> SimulationSettings => 
        _database.GetCollection<SimulationSettings>("SimulationSettings");
    
    public IMongoCollection<C2BRegistration> C2BRegistrations => 
        _database.GetCollection<C2BRegistration>("C2BRegistrations");

    public IMongoCollection<B2CTransactionRecord> B2CTransactions =>
        _database.GetCollection<B2CTransactionRecord>("B2CTransactions");

    private void CreateIndexes()
    {
        // Create indexes for TransactionRecord
        var transactionIndexes = Transactions.Indexes;
        
        transactionIndexes.CreateOne(new CreateIndexModel<TransactionRecord>(
            Builders<TransactionRecord>.IndexKeys.Ascending(x => x.MerchantRequestId)));
        
        transactionIndexes.CreateOne(new CreateIndexModel<TransactionRecord>(
            Builders<TransactionRecord>.IndexKeys.Ascending(x => x.CheckoutRequestId)));
        
        transactionIndexes.CreateOne(new CreateIndexModel<TransactionRecord>(
            Builders<TransactionRecord>.IndexKeys.Ascending(x => x.TransactionId)));
        
        transactionIndexes.CreateOne(new CreateIndexModel<TransactionRecord>(
            Builders<TransactionRecord>.IndexKeys.Ascending(x => x.CreatedAt)));

        // Create indexes for B2CTransactionRecord
        var b2cIndexes = B2CTransactions.Indexes;

        b2cIndexes.CreateOne(new CreateIndexModel<B2CTransactionRecord>(
            Builders<B2CTransactionRecord>.IndexKeys.Ascending(x => x.ConversationId)));

        b2cIndexes.CreateOne(new CreateIndexModel<B2CTransactionRecord>(
            Builders<B2CTransactionRecord>.IndexKeys.Ascending(x => x.OriginatorConversationId)));

        b2cIndexes.CreateOne(new CreateIndexModel<B2CTransactionRecord>(
            Builders<B2CTransactionRecord>.IndexKeys.Ascending(x => x.TransactionId)));

        // Create unique index for C2BRegistration ShortCode
        var c2bIndexes = C2BRegistrations.Indexes;
        
        c2bIndexes.CreateOne(new CreateIndexModel<C2BRegistration>(
            Builders<C2BRegistration>.IndexKeys.Ascending(x => x.ShortCode),
            new CreateIndexOptions { Unique = true }));
    }
}
