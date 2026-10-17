using appErasmus.Models;
using appErasmus.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace appErasmus.Controllers
{
    /// <summary>
    /// Controlador principal responsável pela apresentação das páginas públicas estáticas 
    /// (Landing Page, Privacidade, Documentação) e pela gestão de erros genéricos e acessos não autorizados.
    /// Contém também a rotina crítica de inicialização do sistema (First Run Setup).
    /// </summary>
    public class HomeController : Controller
    {
        /// <summary>
        /// Renderiza a página principal (Landing Page) do portal público.
        /// </summary>
        /// <returns>A View inicial da aplicação.</returns>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Renderiza a página com as políticas de privacidade e tratamento de dados.
        /// </summary>
        /// <returns>A View de Privacidade.</returns>
        public IActionResult Privacy()
        {
            return View();
        }

        /// <summary>
        /// Renderiza a página de documentação ou ajuda para os utilizadores do sistema.
        /// </summary>
        /// <returns>A View de Documentação.</returns>
        public IActionResult Documentacao()
        {
            return View();
        }

        /// <summary>
        /// Interceta tentativas de acesso a áreas protegidas e apresenta uma página amigável de "Acesso Negado".
        /// Funciona como página de aterragem para redirecionamentos de segurança (Erro 403).
        /// </summary>
        /// <returns>A View de Acesso Restrito informando o utilizador da falta de permissões.</returns>
        public IActionResult AcessoRestrito() {
            return View();
        }

        /// <summary>
        /// Captura e exibe erros internos da aplicação de forma controlada.
        /// Esconde a "Stack Trace" do utilizador final por motivos de segurança em ambiente de produção,
        /// exibindo apenas um RequestId rastreável.
        /// </summary>
        /// <returns>A View genérica de Erro (Erro 500).</returns>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        // ========================================================================
        // ROTA DE INSTALAÇÃO DO SISTEMA (FIRST RUN SETUP)
        // ========================================================================

        /// <summary>
        /// Rotina de instalação inicial do ecossistema. 
        /// Cria as fundações de segurança (Roles) e a conta primordial de Super Administrador.
        /// Possui uma tranca de segurança absoluta: aborta imediatamente se a tabela de utilizadores não estiver vazia,
        /// garantindo que esta rota nunca possa ser explorada num servidor em produção após o setup inicial.
        /// </summary>
        /// <param name="serviceProvider">Fornecedor de serviços injetado para aceder ao UserManager e RoleManager.</param>
        /// <returns>
        /// Redireciona para o ecrã de Login caso a instalação seja bem-sucedida, 
        /// ou recambia o utilizador para a página de erro/acesso restrito em caso de falha ou bloqueio de segurança.
        /// </returns>
        [AllowAnonymous]
        public async Task<IActionResult> InstalarSistema([FromServices] IServiceProvider serviceProvider) {
            var userManager = serviceProvider.GetRequiredService<UserManager<Utilizador>>();

            // 1. SEGURANÇA MÁXIMA: Se já existir pelo menos 1 utilizador, a porta fecha-se para sempre.
            if (userManager.Users.Any()) {
                TempData["Erro"] = "O sistema já foi inicializado anteriormente. Acesso negado por motivos de segurança.";
                return RedirectToAction("AcessoRestrito", "Home");
            }

            try {
                // 2. A BD está virgem: Cria as Roles e a conta de Super Administrador
                await appErasmus.Data.DbInitializer.SeedRolesAndSuperUserAsync(serviceProvider);

                TempData["MensagemSucesso"] = "Instalação do sistema concluída! Faz login com a conta de Administrador.";

                // Redireciona diretamente para a página de Login nativa do Identity
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }
            catch (Exception ex) {
                LogService.RegistarErro("First Run Setup", ex);
                TempData["Erro"] = "Ocorreu uma falha ao tentar criar as credenciais iniciais. Verifica os logs.";
                return RedirectToAction("Index", "Home");
            }
        }
    }
}
