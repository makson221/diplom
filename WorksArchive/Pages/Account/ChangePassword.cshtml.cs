using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorksArchive.Data;

namespace WorksArchive.Pages.Account;

public class ChangePasswordModel(AppDbContext db) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Вкажіть поточний пароль"), DataType(DataType.Password), Display(Name = "Поточний пароль")]
    public string CurrentPassword { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Вкажіть новий пароль"), MinLength(6, ErrorMessage = "Мінімум 6 символів"),
     DataType(DataType.Password), Display(Name = "Новий пароль")]
    public string NewPassword { get; set; } = "";

    [BindProperty, Compare(nameof(NewPassword), ErrorMessage = "Паролі не співпадають"),
     DataType(DataType.Password), Display(Name = "Повторіть новий пароль")]
    public string ConfirmPassword { get; set; } = "";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await db.Users.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if (user is null) return Forbid();

        var hasher = new PasswordHasher<AppUser>();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, CurrentPassword) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(nameof(CurrentPassword), "Невірний поточний пароль");
            return Page();
        }

        user.PasswordHash = hasher.HashPassword(user, NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();

        // Перевходимо з новим stamp, щоб поточна сесія лишилась активною.
        var identity = new ClaimsIdentity(User.Claims.Where(c => c.Type != "stamp"), CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim("stamp", user.SecurityStamp));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        TempData["Success"] = "Пароль змінено";
        return RedirectToPage("/Index");
    }
}
