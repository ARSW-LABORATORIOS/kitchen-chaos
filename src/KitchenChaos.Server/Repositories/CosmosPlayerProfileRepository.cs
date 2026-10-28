using Microsoft.Azure.Cosmos;
using KitchenChaos.Server.Models;

namespace KitchenChaos.Server.Repositories;

/// <summary>
/// Implementación de <see cref="IPlayerProfileRepository"/> usando Azure Cosmos DB.
/// La base de datos y el contenedor se crean automáticamente si no existen.
/// AB#13
/// </summary>
public class CosmosPlayerProfileRepository : IPlayerProfileRepository
{
    private readonly Container _container;

    public CosmosPlayerProfileRepository(CosmosClient client, IConfiguration config)
    {
        var dbName = config["CosmosDb:DatabaseName"]!;
        var containerName = config["CosmosDb:ContainerName"]!;
        _container = client.GetContainer(dbName, containerName);
    }

    /// <inheritdoc/>
    public async Task<PlayerProfile?> GetByIdAsync(string id)
    {
        try
        {
            var response = await _container.ReadItemAsync<PlayerProfile>(id, new PartitionKey(id));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task CreateAsync(PlayerProfile profile) =>
        await _container.CreateItemAsync(profile, new PartitionKey(profile.Id));

    /// <inheritdoc/>
    public async Task UpdateAsync(PlayerProfile profile) =>
        await _container.ReplaceItemAsync(profile, profile.Id, new PartitionKey(profile.Id));
}
