// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using appErasmus.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace appErasmus.Areas.Identity.Pages.Account {
    [AllowAnonymous]
    public class ExternalLoginModel : PageModel {
        private readonly SignInManager<Utilizador> _signInManager;
        private readonly UserManager<Utilizador> _userManager;
        private readonly IUserStore<Utilizador> _userStore;
        private readonly IUserEmailStore<Utilizador> _emailStore;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ExternalLoginModel> _logger;

        public ExternalLoginModel(
            SignInManager<Utilizador> signInManager,
            UserManager<Utilizador> userManager,
            IUserStore<Utilizador> userStore,
            ILogger<ExternalLoginModel> logger,
            IEmailSender emailSender) {
            _signInManager = signInManager;
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _logger = logger;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ProviderDisplayName { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public IActionResult OnGet() => RedirectToPage("./Login");

        public IActionResult OnPost(string provider, string returnUrl = null) {
            var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return new ChallengeResult(provider, properties);
        }

        public async Task<IActionResult> OnGetCallbackAsync(string returnUrl = null, string remoteError = null) {
            returnUrl = returnUrl ?? Url.Content("~/");
            if (remoteError != null) {
                ErrorMessage = $"Error from external provider: {remoteError}";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null) {
                ErrorMessage = "Error loading external login information.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            // 1. Tenta fazer login se a conta da Microsoft já estiver associada a um utilizador
            var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (result.Succeeded) {
                _logger.LogInformation("{Name} logged in with {LoginProvider} provider.", info.Principal.Identity.Name, info.LoginProvider);
                return LocalRedirect(returnUrl);
            }
            if (result.IsLockedOut) {
                return RedirectToPage("./Lockout");
            }
            else {
                // 2. Se falhou, significa que é a primeira vez! Vamos registar automaticamente.
                var email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? info.Principal.FindFirstValue(ClaimTypes.Name);

                // VAI BUSCAR O NOME REAL À MICROSOFT
                var nome = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "Utilizador IPT";

                if (email != null) {
                    var user = await _userManager.FindByEmailAsync(email);

                    if (user != null && !string.IsNullOrWhiteSpace(nome) && (string.IsNullOrWhiteSpace(user.NomeCompleto) || user.NomeCompleto.Equals(user.Email, StringComparison.OrdinalIgnoreCase))) {
                        user.NomeCompleto = nome;
                        await _userManager.UpdateAsync(user);
                    }

                    if (user == null) {
                        // CRIA O UTILIZADOR COM A NOSSA NOVA FUNÇÃO INTELIGENTE!
                        user = CreateUser(email, nome);

                        await _userStore.SetUserNameAsync(user, email, CancellationToken.None);
                        await _emailStore.SetEmailAsync(user, email, CancellationToken.None);

                        user.EmailConfirmed = true;

                        var createResult = await _userManager.CreateAsync(user);
                        if (!createResult.Succeeded) {
                            ErrorMessage = "Ocorreu um erro ao criar a conta automaticamente.";
                            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
                        }

                        // --- PROTEÇÃO FIRST RUN SETUP ---
                        if (user is Aluno) {
                            try {
                                await _userManager.AddToRoleAsync(user, "Aluno");
                            }
                            catch (InvalidOperationException) {
                                await _userManager.DeleteAsync(user);
                                ErrorMessage = "A plataforma Erasmus@IPT ainda não se encontra inicializada ou está em manutenção. Por favor, contacta o administrador ou tenta novamente mais tarde.";
                                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
                            }
                        }
                    }

                    var addLoginResult = await _userManager.AddLoginAsync(user, info);
                    if (!addLoginResult.Succeeded) {
                        ErrorMessage = "Ocorreu um erro ao associar o login da Microsoft.";
                        return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
                    }

                    await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
                    _logger.LogInformation("Utilizador criado e autenticado automaticamente com a Microsoft.");

                    TempData["FirstLogin"] = "true";

                    return RedirectToPage("/Account/Manage/Index", new { area = "Identity" });
                    //return LocalRedirect(returnUrl);
                }

                ReturnUrl = returnUrl;
                ProviderDisplayName = info.ProviderDisplayName;
                return Page();
            }
        }

        public async Task<IActionResult> OnPostConfirmationAsync(string returnUrl = null) {
            returnUrl = returnUrl ?? Url.Content("~/");
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null) {
                ErrorMessage = "Error loading external login information during confirmation.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            if (ModelState.IsValid) {
                // VAI BUSCAR O NOME REAL À MICROSOFT AQUI TAMBÉM
                var nome = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "Utilizador IPT";

                // CRIA COM A FUNÇÃO INTELIGENTE
                var user = CreateUser(Input.Email, nome);

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

                var result = await _userManager.CreateAsync(user);
                if (result.Succeeded) {

                    // --- PROTEÇÃO FIRST RUN SETUP ---
                    if (user is Aluno) {
                        try {
                            await _userManager.AddToRoleAsync(user, "Aluno");
                        }
                        catch (InvalidOperationException) {
                            await _userManager.DeleteAsync(user);
                            ErrorMessage = "A plataforma Erasmus@IPT ainda não se encontra inicializada ou está em manutenção. Por favor, contacta o administrador ou tenta novamente mais tarde.";
                            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
                        }
                    }

                    result = await _userManager.AddLoginAsync(user, info);
                    if (result.Succeeded) {
                        _logger.LogInformation("User created an account using {Name} provider.", info.LoginProvider);

                        var userId = await _userManager.GetUserIdAsync(user);
                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                        var callbackUrl = Url.Page(
                            "/Account/ConfirmEmail",
                            pageHandler: null,
                            values: new { area = "Identity", userId = userId, code = code },
                            protocol: Request.Scheme);

                        await _emailSender.SendEmailAsync(Input.Email, "Confirm your email",
                            $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                        if (_userManager.Options.SignIn.RequireConfirmedAccount) {
                            return RedirectToPage("./RegisterConfirmation", new { Email = Input.Email });
                        }

                        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
                        return LocalRedirect(returnUrl);
                    }
                }
                foreach (var error in result.Errors) {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            ProviderDisplayName = info.ProviderDisplayName;
            ReturnUrl = returnUrl;
            return Page();
        }

        // --- A NOSSA MÁQUINA INTELIGENTE ESTÁ AQUI EM BAIXO! ---
        private Utilizador CreateUser(string email, string nomeReal) {
            try {
                // Se o email existir e começar por "aluno", instanciamos o ALUNO!
                if (!string.IsNullOrEmpty(email) && email.ToLower().StartsWith("aluno")) {
                    // Limpa todas as letras e extrai só o número! (ex: aluno21590@ipt.pt vira 21590)
                    string apenasNumeros = new String(email.Where(Char.IsDigit).ToArray());
                    int.TryParse(apenasNumeros, out int numeroAlunoExtraido);

                    return new Aluno {
                        NomeCompleto = string.IsNullOrEmpty(nomeReal) ? "Aluno IPT" : nomeReal,
                        NumeroAluno = numeroAlunoExtraido,
                        AnoCurricular = 1
                    };
                }

                // Caso contrário (GRI, Professores, Coordenadores), instanciamos o UTILIZADOR normal
                return new Utilizador {
                    NomeCompleto = string.IsNullOrEmpty(nomeReal) ? "Funcionário IPT" : nomeReal
                };
            }
            catch {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(Utilizador)}'. " +
                    $"Ensure that '{nameof(Utilizador)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the external login page in /Areas/Identity/Pages/Account/ExternalLogin.cshtml");
            }
        }

        private IUserEmailStore<Utilizador> GetEmailStore() {
            if (!_userManager.SupportsUserEmail) {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<Utilizador>)_userStore;
        }
    }
}