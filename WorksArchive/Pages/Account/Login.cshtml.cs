using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Data;

namespace WorksArchive.Pages.Account;

[EnableRateLimiting("login")]
public class LoginModel(AppDbContext db) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Вкажіть логін"), Display(Name = "Логін")]
    public string LoginName { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Вкажіть пароль"), DataType(DataType.Password), Display(Name = "Пароль")]
    public string Password { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var login = LoginName.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Login == login);
        var hasher = new PasswordHasher<AppUser>();
        var result = user is null ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, Password);

        if (user is null || result == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("", "Невірний логін або пароль");
            return Page();
        }
        if (!user.IsActive)
        {
            ModelState.AddModelError("", "Обліковий запис заблоковано. Зверніться до адміністратора.");
            return Page();
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, Password);
            await db.SaveChangesAsync();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("login", user.Login),
            new("stamp", user.SecurityStamp)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)) return LocalRedirect(ReturnUrl);
        return RedirectToPage(user.Role == UserRole.Admin ? "/Admin/Index" : "/Works/Index");
    }
}
