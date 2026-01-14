using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Viv.Vva.Enums;

namespace Viv.Vva
{
    public class EncrypOptions
    {
        public EncrypOptions(EncrypType encrypType)
        {
            IV = encrypType == EncrypType.AES ? "0000000000000000" : "00000000";
        }

        public string IV { get; set; }

        public CipherMode Mode { get; set; } = CipherMode.CBC;

        public PaddingMode PaddingMode { get; set; } = PaddingMode.PKCS7;
    }
}
