using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Kartist.Controllers.Base
{
    /// <summary>
    /// Bütün Kartist Controller'larının miras alacağı, ortak Identity/Oturum işlemlerini yöneten temel Controller.
    /// Clean Architecture Prensibi: Tekrar eden kodu aradan kaldır (DRY).
    /// </summary>
    public abstract class BaseController : Controller
    {
        protected int CurrentUserId
        {
            get
            {
                if (User?.Identity?.IsAuthenticated == true)
                {
                    var idClaim = User.Claims.FirstOrDefault(c => c.Type == "Id");
                    if (idClaim != null && int.TryParse(idClaim.Value, out int id))
                    {
                        return id;
                    }
                }
                return 0;
            }
        }

        protected string CurrentUserEmail
        {
            get
            {
                if (User?.Identity?.IsAuthenticated == true)
                {
                    return User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value 
                        ?? User.Identity.Name;
                }
                return null;
            }
        }
    }
}
