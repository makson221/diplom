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

namespace WorksArchive.Pages;

/// <summary>Майстер першого запуску: створення адміністратора, коли в базі ще немає користувачів.</summary>
[EnableRateLimiting("login")]
public class SetupModel(AppDbContext db, SetupState setup, ILogger<SetupModel> log) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Вкажіть ПІБ"), StringLength(150), Display(Name = "Ваше ПІБ")]
    public string FullName { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Вкажіть логін"), StringLength(64, MinimumLength = 3, ErrorMessage = "Логін: 3–64 символи"),
     RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Логін: лише латиниця, цифри, . _ -"), Display(Name = "Логін")]
    public string LoginName { get; set; } = "admin";

    [BindProperty, Required(ErrorMessage = "Вкажіть пароль"), MinLength(8, ErrorMessage = "Пароль: мінімум 8 символів"),
     DataType(DataType.Password), Display(Name = "Пароль")]
    public string Password { get; set; } = "";

    [BindProperty, Compare(nameof(Password), ErrorMessage = "Паролі не співпадають"),
     DataType(DataType.Password), Display(Name = "Повторіть пароль")]
    public string ConfirmPassword { get; set; } = "";

    public IActionResult OnGet() => setup.NeedsSetup ? Page() : Redirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        if (!setup.NeedsSetup || await db.Users.AnyAsync())
        {
            setup.NeedsSetup = false;
            return Redirect("/");
        }
        if (!ModelState.IsValid) return Page();

        var admin = new AppUser { Login = LoginName.Trim(), FullName = FullName.Trim(), Role = UserRole.Admin };
        admin.PasswordHash = new PasswordHasher<AppUser>().HashPassword(admin, Password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        setup.NeedsSetup = false;
        log.LogInformation("Майстер першого запуску: створено адміністратора {Login}", admin.Login);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Name, admin.FullName),
            new(ClaimTypes.Role, admin.Role.ToString()),
            new("login", admin.Login),
            new("stamp", admin.SecurityStamp)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

        TempData["Success"] = "Адміністратора створено! Додайте групи, а потім — викладачів.";
        return RedirectToPage("/Admin/Groups");
    }
}
