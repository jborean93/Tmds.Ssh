// This file is part of Tmds.Ssh which is released under MIT.
// See file LICENSE for full license details.

namespace Tmds.Ssh;

[Flags]
enum SftpOpenFlags : uint
{
    Read = 1,
    Write = 2,

    Append = 4,
    Truncate = 16,

    Open = 0,
    OpenOrCreate = 8,
    CreateNew = 8 | 32,
}