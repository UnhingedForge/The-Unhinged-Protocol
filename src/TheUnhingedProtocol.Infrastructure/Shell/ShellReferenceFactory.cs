using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Infrastructure.Shell;

/// <summary>
/// Creates non-destructive references only after validating the current shell target.
/// </summary>
public static class ShellReferenceFactory
{
    public static ItemReference CreateAvailable(string target, ItemKind kind)
    {
        if (kind != ItemKind.Url)
        {
            bool exists = kind == ItemKind.Folder ? Directory.Exists(target) : File.Exists(target);
            if (!exists)
            {
                throw new FileNotFoundException("The selected shell item is unavailable.", target);
            }
        }

        return ItemReference.Create(target, kind);
    }
}
