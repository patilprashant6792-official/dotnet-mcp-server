namespace MCP.Core.Exceptions;

public class NuGetServiceException : Exception
{
    public string? PackageId { get; }
    public string? Version { get; }

    public NuGetServiceException(string message, string? packageId = null, string? version = null)
        : base(message)
    {
        PackageId = packageId;
        Version = version;
    }

    public NuGetServiceException(string message, Exception innerException, string? packageId = null, string? version = null)
        : base(message, innerException)
    {
        PackageId = packageId;
        Version = version;
    }
}

public class PackageTooLargeException : NuGetServiceException
{
    public long ActualSize { get; }
    public long MaxSize { get; }

    public PackageTooLargeException(string packageId, long actualSize, long maxSize)
        : base($"Package '{packageId}' size ({actualSize / (1024 * 1024)}MB) exceeds maximum allowed ({maxSize / (1024 * 1024)}MB)", packageId)
    {
        ActualSize = actualSize;
        MaxSize = maxSize;
    }
}

public class ServiceCapacityException : NuGetServiceException
{
    public ServiceCapacityException(string message)
        : base(message)
    {
    }
}

public class InvalidPackageException : NuGetServiceException
{
    public InvalidPackageException(string message, string packageId)
        : base(message, packageId)
    {
    }
}

/// <summary>
/// Push test
/// </summary>
public class PackageDownloadException : NuGetServiceException
{
    public PackageDownloadException(string message, string packageId, string? version, Exception? innerException = null)
        : base(message, innerException, packageId, version)
    {
    }
}