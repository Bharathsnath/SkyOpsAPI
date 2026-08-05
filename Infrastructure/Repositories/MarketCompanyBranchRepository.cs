using Microsoft.Extensions.Configuration;
using MySqlConnector;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public class MarketCompanyBranchRepository : IMarketCompanyBranchRepository
{
    private readonly string _connectionString;

    public MarketCompanyBranchRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("SkyOpsDBconnection")
            ?? throw new InvalidOperationException("SkyOpsDBconnection not configured");
    }

    public async Task<IReadOnlyList<MarketMaster>> GetMarketsAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = "SELECT Id, MarketName,MarketCode, IsActive FROM MarketMaster WHERE IsActive = 1 ORDER BY MarketName";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        var list = new List<MarketMaster>();
        while (await rdr.ReadAsync(ct))
            list.Add(new MarketMaster
            {
                Id = rdr.GetInt32("Id"),
                MarketName = rdr.GetString("MarketName"),
                MarketCode = rdr.GetString("MarketCode"),
                IsActive = rdr.GetBoolean("IsActive")
            });
        return list;
    }

    public async Task<IReadOnlyList<CompanyMaster>> GetCompaniesAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = "SELECT Id, MarketId,CompanyCode, CompanyName, IsActive FROM CompanyMaster ORDER BY CompanyName";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        var list = new List<CompanyMaster>();
        while (await rdr.ReadAsync(ct))
            list.Add(new CompanyMaster
            {
                Id = rdr.GetInt32("Id"),
                MarketId = rdr.GetInt32("MarketId"),
                CompanyCode = rdr.GetString("CompanyCode"),
                CompanyName = rdr.GetString("CompanyName"),
                IsActive = rdr.GetBoolean("IsActive")
            });
        return list;
    }

    public async Task<IReadOnlyList<BranchMaster>> GetBranchesAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = "SELECT Id, CompanyId,BranchCode, BranchName, IsActive FROM BranchMaster ORDER BY BranchCode";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        var list = new List<BranchMaster>();
        while (await rdr.ReadAsync(ct))
            list.Add(new BranchMaster
            {
                Id = rdr.GetInt32("Id"),
                CompanyId = rdr.GetInt32("CompanyId"),
                BranchCode = rdr.GetString("BranchCode"),
                BranchName = rdr.GetString("BranchName"),
                IsActive = rdr.GetBoolean("IsActive")
            });
        return list;
    }
}
