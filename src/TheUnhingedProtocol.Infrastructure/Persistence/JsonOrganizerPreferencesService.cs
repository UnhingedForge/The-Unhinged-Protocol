using System.Text.Json;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Infrastructure.Persistence;

public sealed class JsonOrganizerPreferencesService : IOrganizerPreferencesService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string path;
    private readonly SemaphoreSlim access = new(1, 1);

    public JsonOrganizerPreferencesService(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
    }

    public async Task<OrganizerPreferences> GetAsync(CancellationToken cancellationToken)
    {
        await access.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return new OrganizerPreferences();
            }

            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            OrganizerPreferences preferences = await JsonSerializer.DeserializeAsync<OrganizerPreferences>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The organizer settings file was empty.");
            return (preferences with { SchemaVersion = ContractSchema.CurrentVersion }).EnsureValid();
        }
        finally
        {
            access.Release();
        }
    }

    public async Task<OrganizerPreferences> SaveAsync(
        OrganizerPreferences preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        OrganizerPreferences validated = (preferences with
        {
            SchemaVersion = ContractSchema.CurrentVersion,
        }).EnsureValid();

        await access.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        validated,
                        SerializerOptions,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return validated;
        }
        finally
        {
            access.Release();
        }
    }

    public void Dispose() => access.Dispose();
}
