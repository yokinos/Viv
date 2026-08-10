using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Viv.Delusion
{
    public class EncrypOptions
    {
        public CipherMode Mode { get; set; } = CipherMode.CBC;

        public PaddingMode PaddingMode { get; set; } = PaddingMode.PKCS7;
    }
}
