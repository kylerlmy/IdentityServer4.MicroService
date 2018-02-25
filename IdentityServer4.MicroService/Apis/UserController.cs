﻿using System;
using System.Reflection;
using System.Linq;
using System.Data;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.SwaggerGen;
using Newtonsoft.Json;
using IdentityServer4.MicroService.Enums;
using IdentityServer4.MicroService.Data;
using IdentityServer4.MicroService.Services;
using IdentityServer4.MicroService.CacheKeys;
using IdentityServer4.MicroService.Tenant;
using IdentityServer4.EntityFramework.DbContexts;
using IdentityServer4.MicroService.Models.Apis.Common;
using IdentityServer4.MicroService.Models.Apis.UserController;
using static IdentityServer4.MicroService.AppConstant;
using static IdentityServer4.MicroService.MicroserviceConfig;
using Microsoft.Extensions.Caching.Distributed;

namespace IdentityServer4.MicroService.Apis
{
    // user 根据 tenantId 来获取列表、或详情、增删改

    /// <summary>
    /// 用户
    /// </summary>
    [Route("User")]
    [Produces("application/json")]
    [Authorize(AuthenticationSchemes = AppAuthenScheme, Roles = Roles.Users)]
    public class UserController : BasicController
    {
        #region Services
        //Database
        readonly IdentityDbContext db;
        // 短信
        readonly ISmsSender sms;
        // 邮件
        readonly IEmailSender email;
        // 加解密
        readonly ITimeLimitedDataProtector protector;
        // 用户管理SDK
        readonly UserManager<AppUser> userManager;

        readonly TenantDbContext tenantDbContext;

        readonly ConfigurationDbContext configDbContext;
        #endregion

        public UserController(
            IdentityDbContext _db,
            RedisService _redis,
            IStringLocalizer<UserController> _localizer,
            ISmsSender _sms,
            IEmailSender _email,
            IDataProtectionProvider _provider,
            UserManager<AppUser> _userManager,
            TenantDbContext _tenantDbContext,
            ConfigurationDbContext _configDbContext)
        {
            // 多语言
            l = _localizer;
            db = _db;
            redis = _redis;
            sms = _sms;
            protector = _provider.CreateProtector(GetType().FullName).ToTimeLimitedDataProtector();
            email = _email;
            userManager = _userManager;
            tenantDbContext = _tenantDbContext;
            configDbContext = _configDbContext;
        }


        /// <summary>
        /// 用户 - 列表
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = AppAuthenScheme, Policy = UserPermissions.Read)]
        [SwaggerOperation("User/Get")]
        public async Task<PagingResult<View_User>> Get(PagingRequest<UserGetRequest> value)
        {
            if (!ModelState.IsValid)
            {
                return new PagingResult<View_User>()
                {
                    code = (int)BasicControllerEnums.UnprocessableEntity,

                    message = ModelErrors()
                };
            }

            if (string.IsNullOrWhiteSpace(value.orderby))
            {
                value.orderby = "UserId";
            }

            var q = new PagingService<View_User>(db, value, "View_User")
            {
                where = (where, sqlParams) =>
                {
                    where.Add("TenantId = " + TenantId);

                    if (!string.IsNullOrWhiteSpace(value.q.email))
                    {
                        where.Add("Email = @Email");
                        sqlParams.Add(new SqlParameter("@Email", value.q.email));
                    }

                    if (!string.IsNullOrWhiteSpace(value.q.name))
                    {
                        where.Add("UserName like @UserName");
                        sqlParams.Add(new SqlParameter("@UserName", "%" + value.q.name + "%"));
                    }

                    if (!string.IsNullOrWhiteSpace(value.q.phoneNumber))
                    {
                        where.Add("PhoneNumber = @PhoneNumber");
                        sqlParams.Add(new SqlParameter("@PhoneNumber", value.q.phoneNumber));
                    }

                    if (value.q.roles != null && value.q.roles.Count > 0)
                    {
                        var rolesExpression = new List<string>();

                        value.q.roles.ForEach(r =>
                        {
                            rolesExpression.Add("Roles Like '%\"Id\":" + r + ",%'");
                        });

                        where.Add(" ( " + string.Join(" OR ", rolesExpression) + " ) ");
                    }
                }
            };

            var result = await q.ExcuteAsync(propConverter: (prop, val) =>
             {
                 if (prop.Name.Equals("Roles"))
                 {
                     return JsonConvert.DeserializeObject<List<View_User_Role>>(val.ToString());
                 }
                 else if (prop.Name.Equals("Claims"))
                 {
                     return JsonConvert.DeserializeObject<List<View_User_Claim>>(val.ToString());
                 }
                 else if (prop.Name.Equals("Files"))
                 {
                     return JsonConvert.DeserializeObject<List<View_User_File>>(val.ToString());
                 }
                 return val;
             });

            return result;
        }

        /// <summary>
        ///用户 - 详情
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet("{id}")]
        [Authorize(AuthenticationSchemes = AppAuthenScheme, Policy = ClientScopes.Read)]
        [SwaggerOperation("User/Detail")]
        public async Task<ApiResult<AppUser>> Get(int id)
        {
            var query = db.Users.AsQueryable();

            query = query.Where(x => x.Tenants.Any(t => t.TenantId == TenantId));

            var entity = await query
                .Include(x => x.Logins)
                .Include(x => x.Claims)
                .Include(x => x.Roles)
                .Include(x => x.Files)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (entity == null)
            {
                return new ApiResult<AppUser>(l, BasicControllerEnums.NotFound);
            }

            return new ApiResult<AppUser>(entity);
        }

        ///// <summary>
        ///// 用户 - 创建
        ///// </summary>
        ///// <param name = "value" ></ param >
        ///// < returns ></ returns >
        ////[HttpPost]
        ////[Authorize(AuthenticationSchemes = AppAuthenScheme, Policy = UserPermissions.Create)]
        ////[SwaggerOperation("User/Post")]
        ////public async Task<ApiResult<long>> Post([FromBody]AppUser value)
        ////{
        ////    if (!ModelState.IsValid)
        ////    {
        ////        return new ApiResult<long>(l, BasicControllerEnums.UnprocessableEntity,
        ////            ModelErrors());
        ////    }

        ////    await AccountService.CreateUser(TenantId,
        ////        null,
        ////        db,
        ////        tenantDb,
        ////        null,
        ////        value,
        ////        UserId);

        ////    db.Add(value);

        ////    await db.SaveChangesAsync();

        ////    return new ApiResult<long>(value.Id);
        ////}

        /// <summary>
        /// 用户 - 更新
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpPut]
        [Authorize(AuthenticationSchemes = AppAuthenScheme, Policy = UserPermissions.Update)]
        [SwaggerOperation("User/Put")]
        public async Task<ApiResult<long>> Put([FromBody]AppUser value)
        {
            if (!ModelState.IsValid)
            {
                return new ApiResult<long>(l,
                    BasicControllerEnums.UnprocessableEntity,
                    ModelErrors());
            }

            using (var tran = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    #region Update Entity
                    // 需要先更新value，否则更新如claims等属性会有并发问题
                    db.Update(value);
                    db.SaveChanges();
                    #endregion

                    #region Find Entity.Source
                    var source = await db.Users.Where(x => x.Id == value.Id)
                                     .Include(x => x.Logins)
                                     .Include(x => x.Claims)
                                     .Include(x => x.Roles)
                                     .Include(x => x.Files)
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync();
                    #endregion

                    #region Update Entity.Claims
                    if (value.Claims != null && value.Claims.Count > 0)
                    {
                        #region delete
                        var EntityIDs = value.Claims.Select(x => x.Id).ToList();
                        if (EntityIDs.Count > 0)
                        {
                            var DeleteEntities = source.Claims.Where(x => !EntityIDs.Contains(x.Id)).Select(x => x.Id).ToArray();

                            if (DeleteEntities.Count() > 0)
                            {
                                var sql = string.Format("DELETE AspNetUserClaims WHERE ID IN ({0})",
                                            string.Join(",", DeleteEntities));

                                db.Database.ExecuteSqlCommand(new RawSqlString(sql));
                            }
                        }
                        #endregion

                        #region update
                        var UpdateEntities = value.Claims.Where(x => x.Id > 0).ToList();
                        if (UpdateEntities.Count > 0)
                        {
                            UpdateEntities.ForEach(x =>
                            {
                                db.Database.ExecuteSqlCommand(
                                  new RawSqlString("UPDATE AspNetUserClaims SET [ClaimType]=@Type,[ClaimValue]=@Value WHERE Id = " + x.Id),
                                  new SqlParameter("@Type", x.ClaimType),
                                  new SqlParameter("@Value", x.ClaimValue));
                            });
                        }
                        #endregion

                        #region insert
                        var NewEntities = value.Claims.Where(x => x.Id == 0).ToList();
                        if (NewEntities.Count > 0)
                        {
                            NewEntities.ForEach(x =>
                            {
                                db.Database.ExecuteSqlCommand(
                                  new RawSqlString("INSERT INTO AspNetUserClaims VALUES (@ClaimType,@ClaimValue,@UserId)"),
                                  new SqlParameter("@ClaimType", x.ClaimType),
                                  new SqlParameter("@ClaimValue", x.ClaimValue),
                                  new SqlParameter("@UserId", source.Id));
                            });
                        }
                        #endregion
                    }
                    #endregion

                    #region Update Entity.Files
                    if (value.Files != null && value.Files.Count > 0)
                    {
                        #region delete
                        var EntityIDs = value.Files.Select(x => x.Id).ToList();
                        if (EntityIDs.Count > 0)
                        {
                            var DeleteEntities = source.Files.Where(x => !EntityIDs.Contains(x.Id)).Select(x => x.Id).ToArray();

                            if (DeleteEntities.Count() > 0)
                            {
                                var sql = string.Format("DELETE AspNetUserFile WHERE ID IN ({0})",
                                            string.Join(",", DeleteEntities));

                                db.Database.ExecuteSqlCommand(new RawSqlString(sql));
                            }
                        }
                        #endregion

                        #region update
                        var UpdateEntities = value.Files.Where(x => x.Id > 0).ToList();
                        if (UpdateEntities.Count > 0)
                        {
                            UpdateEntities.ForEach(x =>
                            {
                                db.Database.ExecuteSqlCommand(
                                  new RawSqlString("UPDATE AspNetUserFile SET [FileType]=@FileType,[Files]=@Files WHERE Id = " + x.Id),
                                  new SqlParameter("@FileType", x.FileType),
                                  new SqlParameter("@Files", x.Files));
                            });
                        }
                        #endregion

                        #region insert
                        var NewEntities = value.Files.Where(x => x.Id == 0).ToList();
                        if (NewEntities.Count > 0)
                        {
                            NewEntities.ForEach(x =>
                            {
                                db.Database.ExecuteSqlCommand(
                                  new RawSqlString("INSERT INTO AspNetUserFile VALUES (@FileType,@Files,@AppUserId)"),
                                  new SqlParameter("@FileType", x.FileType),
                                  new SqlParameter("@Files", x.Files),
                                  new SqlParameter("@AppUserId", source.Id));
                            });
                        }
                        #endregion
                    }
                    #endregion

                    #region Update Entity.Roles
                    if (value.Roles != null && value.Roles.Count > 0)
                    {
                        #region delete
                        var sql = $"DELETE AspNetUserRoles WHERE UserId = {source.Id}";
                        db.Database.ExecuteSqlCommand(new RawSqlString(sql));
                        #endregion

                        #region insert
                        value.Roles.ForEach(x =>
                        {
                            db.Database.ExecuteSqlCommand(
                              new RawSqlString("INSERT INTO AspNetUserRoles VALUES (@UserId,@RoleId)"),
                              new SqlParameter("@UserId", source.Id),
                              new SqlParameter("@RoleId", x.RoleId));
                        });
                        #endregion
                    }
                    #endregion

                    #region Update Entity.Roles
                    if (value.Roles != null && value.Roles.Count > 0)
                    {
                        #region delete
                        var sql = $"DELETE AspNetUserRoles WHERE UserId = {source.Id}";
                        db.Database.ExecuteSqlCommand(new RawSqlString(sql));
                        #endregion

                        #region insert
                        value.Roles.ForEach(x =>
                        {
                            db.Database.ExecuteSqlCommand(
                              new RawSqlString("INSERT INTO AspNetUserRoles VALUES (@UserId,@RoleId)"),
                              new SqlParameter("@UserId", source.Id),
                              new SqlParameter("@RoleId", x.RoleId));
                        });
                        #endregion
                    }
                    #endregion

                    tran.Commit();
                }

                catch (Exception ex)
                {
                    tran.Rollback();

                    return new ApiResult<long>(l,
                        BasicControllerEnums.ExpectationFailed,
                        ex.Message);
                }
            }

            return new ApiResult<long>(value.Id);
        }

        /// <summary>
        /// 用户 - 删除
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        [Authorize(AuthenticationSchemes = AppAuthenScheme, Policy = UserPermissions.Delete)]
        [SwaggerOperation("User/Delete")]
        public async Task<ApiResult<long>> Delete(int id)
        {
            var query = db.Users.AsQueryable();

            query = query.Where(x => x.Tenants.Any(t => t.TenantId == TenantId));

            var entity = await query.Where(x => x.Id == id).FirstOrDefaultAsync();

            if (entity == null)
            {
                return new ApiResult<long>(l, BasicControllerEnums.NotFound);
            }

            db.Users.Remove(entity);

            await db.SaveChangesAsync();

            return new ApiResult<long>(id);
        }

        /// <summary>
        /// 用户 - 是否存在
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpGet("Head")]
        [Authorize(AuthenticationSchemes = AppAuthenScheme, Policy = ClientScopes.Read)]
        [SwaggerOperation("User/Head")]
        public async Task<ObjectResult> Head(UserDetailRequest value)
        {
            if (!ModelState.IsValid)
            {
                return new BadRequestObjectResult(0);
            }

            var query = db.Users.AsQueryable();

            query = query.Where(x => x.Tenants.Any(t => t.TenantId == TenantId));

            var result = await query.Where(x => x.PhoneNumber.Equals(value.PhoneNumber))
                .Select(x => x.Id).FirstOrDefaultAsync();

            if (result > 0)
            {
                return new OkObjectResult(result);
            }
            else
            {
                return new NotFoundObjectResult(0);
            }
        }

        #region 用户注册
        /// <summary>
        /// 用户注册，需验证手机号和邮箱
        /// </summary>
        /// <returns></returns>
        [HttpPost("Register")]
        [Authorize(AuthenticationSchemes = AppAuthenScheme, Policy = ClientScopes.Create)]
        [SwaggerOperation("User/Register")]
        public async Task<ApiResult<string>> Register([FromBody]UserRegisterRequest value)
        {
            if (!ModelState.IsValid)
            {
                return new ApiResult<string>(l, BasicControllerEnums.UnprocessableEntity,
                    ModelErrors());
            }

            #region 校验邮箱是否重复
            if (await db.Users.AnyAsync(x => x.Email.Equals(value.Email)))
            {
                return new ApiResult<string>(l,UserControllerEnum.Register_EmailExists);
            }
            #endregion
            #region 校验邮箱验证码
            if (!string.IsNullOrWhiteSpace(value.EmailVerifyCode))
            {
                try
                {
                    protector.Unprotect(value.EmailVerifyCode);
                }
                catch
                {
                    return new ApiResult<string>(l,UserControllerEnum.Register_EmailVerifyCodeError);
                }
            }
            #endregion

            #region 校验手机号是否重复
            if (await db.Users.AnyAsync(x => x.PhoneNumber.Equals(value.PhoneNumber)))
            {
                return new ApiResult<string>(l, UserControllerEnum.Register_PhoneNumberExists);
            }
            #endregion
            #region 校验手机验证码
            var PhoneNumberVerifyCodeKey = UserControllerKeys.VerifyCode_Phone + value.PhoneNumber + ":" + value.PhoneNumberVerifyCode;

            if (await redis.KeyExistsAsync(PhoneNumberVerifyCodeKey) == false)
            {
                return new ApiResult<string>(l, UserControllerEnum.Register_PhoneNumberVerifyCodeError);
            }

            await redis.RemoveAsync(PhoneNumberVerifyCodeKey);
            #endregion

            #region 创建用户
            var user = new AppUser
            {
                UserName = value.Email,
                Email = value.Email,
                PhoneNumber = value.PhoneNumber,
                NickName = value.NickName,
                Gender = value.Gender,
                Address = value.Address,
                Birthday = value.Birthday,
                PhoneNumberConfirmed = true,
                Stature = value.Stature,
                Weight = value.Weight,
                Description = value.Description,
                CreateDate = DateTime.UtcNow,
                LastUpdateTime = DateTime.UtcNow,
                EmailConfirmed = true,
                ParentUserID = UserId
            };

            #region 确认邮箱验证通过
            //如果填写了邮件验证码，并且验证通过（不通过不会走到这里）
            if (!string.IsNullOrWhiteSpace(value.EmailVerifyCode))
            {
                user.EmailConfirmed = true;
            }
            #endregion

            #region 图片
            if (value.ImageUrl != null && value.ImageUrl.Count > 0)
            {
                user.Files.Add(new AspNetUserFile()
                {
                    Files = JsonConvert.SerializeObject(value.ImageUrl),
                    FileType = FileTypes.Image,
                });
            }
            #endregion

            #region 视频
            if (!string.IsNullOrWhiteSpace(value.Video))
            {
                user.Files.Add(new AspNetUserFile()
                {
                    Files = value.Video,
                    FileType = FileTypes.Video,
                });
            }
            #endregion

            #region 文档
            if (!string.IsNullOrWhiteSpace(value.Doc))
            {
                user.Files.Add(new AspNetUserFile()
                {
                    Files = value.Doc,
                    FileType = FileTypes.Doc,
                });
            }
            #endregion

            var roleIds = db.Roles.Where(x => x.Name.Equals(Roles.Users) || x.Name.Equals(Roles.Developer))
                    .Select(x => x.Id).ToList();

            var permissions = typeof(UserPermissions).GetFields().Select(x => x.GetCustomAttribute<PolicyClaimValuesAttribute>().ClaimsValues[0]).ToList();

            var tenantIds = tenantDbContext.Tenants.Select(x => x.Id).ToList();

            var result = await AccountService.CreateUser(TenantId,
                userManager,
                db,
                user, 
                roleIds, 
                string.Join(",", permissions), 
                tenantIds);

            if (result.Succeeded)
            {
                return new ApiResult<string>();
            }

            else
            {
                return new ApiResult<string>(l, BasicControllerEnums.ExpectationFailed,
                    JsonConvert.SerializeObject(result.Errors));
            }
            #endregion
        }

        /// <summary>
        /// 发送手机验证码
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpPost("VerifyPhone")]
        [Authorize(AuthenticationSchemes = AppAuthenScheme, Policy = ClientScopes.Create)]
        [SwaggerOperation("User/VerifyPhone")]
        public async Task<ApiResult<string>> VerifyPhone([FromBody]UserVerifyPhoneRequest value)
        {
            if (!ModelState.IsValid)
            {
                return new ApiResult<string>(l, BasicControllerEnums.UnprocessableEntity,
                    ModelErrors());
            }

            #region 发送计数、验证是否已经达到上限
            var dailyLimitKey = UserControllerKeys.Limit_24Hour_Verify_Phone + value.PhoneNumber;

            var _dailyLimit = await redis.GetAsync(dailyLimitKey);

            if (!string.IsNullOrWhiteSpace(_dailyLimit))
            {
                var dailyLimit = int.Parse(_dailyLimit);

                if (dailyLimit > UserControllerKeys.Limit_24Hour_Verify_MAX_Phone)
                {
                    return new ApiResult<string>(l, UserControllerEnum.VerifyPhone_CallLimited);
                }
            }
            else
            {
                await redis.SetAsync(dailyLimitKey, "0", TimeSpan.FromHours(24));
            }
            #endregion

            #region 验证发送间隔时间是否过快
            //两次发送间隔必须大于指定秒数
            var _lastTimeKey = UserControllerKeys.LastTime_SendCode_Phone + value.PhoneNumber;

            var lastTimeString = await redis.GetAsync(_lastTimeKey);

            if (!string.IsNullOrWhiteSpace(lastTimeString))
            {
                var lastTime = long.Parse(lastTimeString);

                var now = DateTime.UtcNow.AddHours(8).Ticks;

                var usedTime = (now - lastTime) / 10000000;

                if (usedTime < UserControllerKeys.MinimumTime_SendCode_Phone)
                {
                    return new ApiResult<string>(l, UserControllerEnum.VerifyPhone_TooManyRequests, string.Empty,
                        UserControllerKeys.MinimumTime_SendCode_Phone - usedTime);
                }
            }
            #endregion

            #region 发送验证码
            var verifyCode = random.Next(1111, 9999).ToString();
            var smsVars = JsonConvert.SerializeObject(new { code = verifyCode });
            await sms.SendSmsWithRetryAsync(smsVars, value.PhoneNumber, "9900", 3);
            #endregion

            var verifyCodeKey = UserControllerKeys.VerifyCode_Phone + value.PhoneNumber + ":" + verifyCode;

            // 记录验证码，用于提交报名接口校验
            await redis.SetAsync(verifyCodeKey, string.Empty, TimeSpan.FromSeconds(UserControllerKeys.VerifyCode_Expire_Phone));

            // 记录发送验证码的时间，用于下次发送验证码校验间隔时间
            await redis.SetAsync(_lastTimeKey, DateTime.UtcNow.AddHours(8).Ticks.ToString(), null);

            // 叠加发送次数
            await redis.IncrementAsync(dailyLimitKey);

            return new ApiResult<string>();
        }

        /// <summary>
        /// 发送邮件验证码
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpPost("VerifyEmail")]
        [Authorize(AuthenticationSchemes = AppAuthenScheme, Policy = ClientScopes.Create)]
        [SwaggerOperation("User/VerifyEmail")]
        public async Task<ApiResult<string>> VerifyEmail([FromBody]UserVerifyEmailRequest value)
        {
            if (!ModelState.IsValid)
            {
                return new ApiResult<string>(l, BasicControllerEnums.UnprocessableEntity,
                    ModelErrors());
            }

            #region 发送计数、验证是否已经达到上限
            var dailyLimitKey = UserControllerKeys.Limit_24Hour_Verify_Email + value.Email;

            var _dailyLimit = await redis.GetAsync(dailyLimitKey);

            if (!string.IsNullOrWhiteSpace(_dailyLimit))
            {
                var dailyLimit = int.Parse(_dailyLimit);

                if (dailyLimit > UserControllerKeys.Limit_24Hour_Verify_MAX_Email)
                {
                    return new ApiResult<string>(l, UserControllerEnum.VerifyEmail_CallLimited);
                }
            }
            else
            {
                await redis.SetAsync(dailyLimitKey, "0", TimeSpan.FromHours(24));
            }
            #endregion

            #region 验证发送间隔时间是否过快
            //两次发送间隔必须大于指定秒数
            var _lastTimeKey = UserControllerKeys.LastTime_SendCode_Email + value.Email;

            var lastTimeString = await redis.GetAsync(_lastTimeKey);

            if (!string.IsNullOrWhiteSpace(lastTimeString))
            {
                var lastTime = long.Parse(lastTimeString);

                var now = DateTime.UtcNow.AddHours(8).Ticks;

                var usedTime = (now - lastTime) / 10000000;

                if (usedTime < UserControllerKeys.MinimumTime_SendCode_Email)
                {
                    return new ApiResult<string>(l, UserControllerEnum.VerifyEmail_TooManyRequests, string.Empty,
                        UserControllerKeys.MinimumTime_SendCode_Email - usedTime);
                }
            }
            #endregion

            #region 发送验证码
            var verifyCode = random.Next(111111, 999999).ToString();
            // 用加密算法生成具有时效性的密文
            var protectedData = protector.Protect(verifyCode, TimeSpan.FromSeconds(UserControllerKeys.VerifyCode_Expire_Email));
            var xsmtpapi = JsonConvert.SerializeObject(new
            {
                to = new string[] { value.Email },
                sub = new Dictionary<string, string[]>()
                        {
                            { "%code%", new string[] { protectedData } },
                        }
            });
            await email.SendEmailAsync("邮箱验证", "verify_email", xsmtpapi);
            #endregion

            // 记录发送验证码的时间，用于下次发送验证码校验间隔时间
            await redis.SetAsync(_lastTimeKey, DateTime.UtcNow.AddHours(8).Ticks.ToString(), null);

            // 叠加发送次数
            await redis.IncrementAsync(dailyLimitKey);

            return new ApiResult<string>();
        }
        #endregion

        #region 用户 - 错误码表
        /// <summary>
        /// 用户 - 错误码表
        /// </summary>
        [HttpGet("Codes")]
        [AllowAnonymous]
        [SwaggerOperation("User/Codes")]
        public List<ErrorCodeModel> Codes()
        {
            var result = _Codes<UserControllerEnum>();

            return result;
        }
        #endregion
    }
}