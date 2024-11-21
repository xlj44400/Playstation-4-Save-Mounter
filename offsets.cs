using System;
using System.Collections.Generic;
using System.Text;

namespace PS4Saves
{
    class offsets
    {
        // offsets for ps5 4.03 - probably the same for 4.xx
        public const int sceUserServiceGetInitialUser = 0x3290;
        public const int sceUserServiceGetLoginUserIdList = 0x2A50;
        public const int sceUserServiceGetUserName = 0x46E0;

        public const int sceSaveDataMount2 = 0x31470;
        public const int sceSaveDataUmount = 0x31940;
        public const int sceSaveDataDirNameSearch = 0x32720;
        public const int sceSaveDataTransferringMount = 0x317F0;
        public const int sceSaveDataInitialize3 = 0x30FE0;
    }
}
