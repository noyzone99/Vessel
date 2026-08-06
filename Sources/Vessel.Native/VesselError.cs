namespace Vessel.Native;

public enum VesselError
{
    InvalidOperation = -7,
    Disposed = -6,
    IoError = -5,
    AlreadyExists = -4,
    NotFound = -3,
    InvalidArgument = -2,
    UnknownError = -1,
    Success = 0
}
