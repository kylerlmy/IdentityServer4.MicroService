﻿using System.Collections.Generic;

namespace IdentityServer4.MicroService.Models.Apis.Common
{
   public interface IPagingResult
    {
        int take { get; set; }

        int skip { get; set; }

        int code { get; set; }

        int total { get; set; }

        string message { get; set; }
    }
}
