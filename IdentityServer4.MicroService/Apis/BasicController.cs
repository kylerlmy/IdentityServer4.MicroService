﻿using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IdentityServer4.MicroService.Tenant;
using IdentityServer4.MicroService.Services;
using static IdentityServer4.MicroService.AppConstant;
using System.Data.SqlClient;
using System.Data.Common;
using System.Collections.Generic;
using System.Reflection;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using IdentityServer4.MicroService.CacheKeys;
using IdentityServer4.MicroService.Models.Apis.Common;
using IdentityServer4.MicroService.Models.Shared;

namespace IdentityServer4.MicroService.Apis
{
    //[ServiceFilter(typeof(ApiTracker.ApiTracker), IsReusable = true)]
    [Authorize(AuthenticationSchemes = AppAuthenScheme)]
    public class BasicController : ControllerBase
    {
        public virtual IStringLocalizer l { get; set; }
        public virtual RedisService redis { get; set; }

        protected readonly Random random = new Random(DateTime.UtcNow.AddHours(8).Second);

        protected long UserId
        {
            get
            {
                return long.Parse(User.Claims.FirstOrDefault(x => x.Type.Equals("sub")).Value);
            }
        }

        protected string ModelErrors()
        {
            var errObject = new JObject();

            foreach (var errKey in ModelState.Keys)
            {
                var errValues = ModelState[errKey];

                var errMessages = errValues.Errors.Select(x => !string.IsNullOrWhiteSpace(x.ErrorMessage) ? l[x.ErrorMessage] : x.Exception.Message).ToList();

                if (errMessages.Count > 0)
                {
                    errObject.Add(errKey, JToken.FromObject(errMessages));
                }
            }

            return JsonConvert.SerializeObject(errObject);
        }

        /// <summary>
        /// 租户信息 from client access token
        /// </summary>
        protected long TenantId
        {
            get
            {
                var tenant = User.Claims.
                    Where(x => x.Type.Contains(TenantConstant.TokenKey)).FirstOrDefault();

                if (tenant != null)
                {
                    var _tenantId = JObject.Parse(tenant.Value)["id"].ToString();

                    return long.Parse(_tenantId);
                }

                return 1L;
            }
        }

        public virtual TenantService tenantService { get; set; }
        public virtual TenantDbContext tenantDb { get; set; }

        private TenantPrivateModel _tenant;
        public TenantPrivateModel Tenant
        {
            get
            {
                if (_tenant == null)
                {
                    var tenantCache = tenantService.GetTenant(tenantDb, HttpContext.Request.Host.Value);

                    _tenant = JsonConvert.DeserializeObject<TenantPrivateModel>(tenantCache.Item2);
                }

                return _tenant;
            }
        }

        private AzureApiManagementServices _azureApim;
        public AzureApiManagementServices AzureApim
        {
            get
            {
                if (_azureApim == null)
                {
                    if (Tenant.properties.ContainsKey(AzureApiManagementKeys.Host) &&
                    Tenant.properties.ContainsKey(AzureApiManagementKeys.ApiId) &&
                    Tenant.properties.ContainsKey(AzureApiManagementKeys.ApiKey))
                    {
                        _azureApim = new AzureApiManagementServices(
                            Tenant.properties[AzureApiManagementKeys.Host],
                            Tenant.properties[AzureApiManagementKeys.ApiId],
                            Tenant.properties[AzureApiManagementKeys.ApiKey]);
                    }
                }

                return _azureApim;
            }
        }

        /// <summary>
        /// 根据枚举，返回值与名称的字典
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        protected List<ErrorCodeModel> _Codes<T>()
        {
            var t = typeof(T);

            var items = t.GetFields()
                .Where(x => x.CustomAttributes.Count() > 0).ToList();

            var result = new List<ErrorCodeModel>();

            foreach (var item in items)
            {
                var code = long.Parse(item.GetRawConstantValue().ToString());

                var codeName = item.Name;

                var desc = item.GetCustomAttribute<DescriptionAttribute>();

                var codeItem = new ErrorCodeModel()
                {
                    Code = code,
                    Name = codeName,
                    Description = l != null ? l[desc.Description] : desc.Description
                };

                result.Add(codeItem);
            }

            return result;
        }

        protected object ExecuteScalar(DbContext db, string sql, params SqlParameter[] sqlparams)
        {
            using (var connection = db.Database.GetDbConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;

                    command.Parameters.AddRange(sqlparams);

                    return command.ExecuteScalar();
                }
            }
        }
        protected void ExecuteReader(DbContext db, string sql, Action<DbDataReader> action, params SqlParameter[] sqlparams)
        {
            using (var connection = db.Database.GetDbConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;

                    command.Parameters.AddRange(sqlparams);


                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {

                        }
                    }
                }
            }
        }
    }
}