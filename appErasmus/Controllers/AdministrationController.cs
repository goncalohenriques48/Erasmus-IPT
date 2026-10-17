using appErasmus.Data;
using appErasmus.Models;
using appErasmus.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace appErasmus.Controllers
{
    /// <summary>
    /// Controller responsável pela área de administração e parametrização global do ecossistema Erasmus@IPT.
    /// Acesso estritamente restrito a utilizadores com a role de "Admin".
    /// Permite gerir privilégios de acesso (GRI, Coordenadores, Administradores) e inicializar a Base de Dados.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class AdministrationController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<Utilizador> _userManager;

        /// <summary>
        /// Inicializa o controller com o contexto da base de dados e o gestor de utilizadores.
        /// </summary>
        /// <param name="context">Contexto da aplicação usado para aceder aos cursos.</param>
        /// <param name="userManager">Serviço do Identity usado para criar e gerir utilizadores.</param>
        public AdministrationController(ApplicationDbContext context, UserManager<Utilizador> userManager) {
            _context = context;
            _userManager = userManager;
        }

        // GET: Administration/
        /// <summary>
        /// Mostra a página principal de administração com as respectivas
        /// informações e ações possiveis (Ex: atribuir coordenadores Erasmus aos cursos ativos).
        /// </summary>
        /// <returns>View de administração com a lista de cursos disponíveis.</returns>
        public async Task<IActionResult> Index() {
            try {
                await CarregarDadosAtribuicaoCoordenadorAsync();
            }
            catch (Exception ex) {
                LogService.RegistarErro("Administration Index GET", ex);
                // Mesmo que falhe a leitura dos cursos, a página carrega na mesma (embora vazia)
            }
            return View();
        }

        // POST: Administration/AtribuirCoordenadorCurso
        /// <summary>
        /// Atribui um coordenador Erasmus a um curso através do email indicado.
        /// Caso o utilizador ainda não exista, cria uma conta base sem password
        /// e associa-lhe a role de CoordenadorErasmus.
        /// </summary>
        /// <param name="cursoId">Identificador do curso a atualizar.</param>
        /// <param name="coordenadorEmail">Email do coordenador a associar ao curso.</param>
        /// <returns>Redirecionamento para a página principal de administração.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AtribuirCoordenadorCurso(int cursoId, string coordenadorEmail) {

            // Garante que foram indicados um curso e um email de coordenador.
            if (cursoId <= 0 || string.IsNullOrWhiteSpace(coordenadorEmail)){
                TempData["Erro"] = "Tem de selecionar um curso e indicar o email do coordenador.";
                return RedirectToAction(nameof(Index));
            }

            try {
                var curso = await _context.Cursos
                .FirstOrDefaultAsync(c => c.Id == cursoId);

                // Impede atribuições a cursos inexistentes ou removidos da base de dados.
                if (curso == null) {
                    TempData["Erro"] = "O curso selecionado não foi encontrado.";
                    return RedirectToAction(nameof(Index));
                }

                var emailNormalizado = coordenadorEmail.Trim();
                var coordenador = await _userManager.FindByEmailAsync(emailNormalizado);

                if (coordenador == null) {
                    coordenador = CriarUtilizadorPorEmail(emailNormalizado);

                    var resultadoCriacao = await _userManager.CreateAsync(coordenador);

                    // Se não conseguir criar a conta deu erro, não associa o curso.
                    if (!resultadoCriacao.Succeeded) {
                        TempData["Erro"] = "Não foi possível criar a conta do utilizador indicado.";
                        return RedirectToAction(nameof(Index));
                    }

                    if (coordenador is Aluno && !await _userManager.IsInRoleAsync(coordenador, "Aluno")) {
                        await _userManager.AddToRoleAsync(coordenador, "Aluno");
                    }
                } else if (curso.CoordenadorFK == coordenador.Id) {
                    TempData["Erro"] = "Este utilizador já é o coordenador deste curso.";
                    return RedirectToAction(nameof(Index));
                }

                // Garante que o utilizador existe com a role de CoordenadorErasmus.
                if (!await _userManager.IsInRoleAsync(coordenador, "CoordenadorErasmus")) {
                    var resultadoRole = await _userManager.AddToRoleAsync(coordenador, "CoordenadorErasmus");

                    if (!resultadoRole.Succeeded) {
                        TempData["Erro"] = "Não foi possível atribuir a role de Coordenador Erasmus ao utilizador.";
                        return RedirectToAction(nameof(Index));
                    }
                }

                curso.CoordenadorFK = coordenador.Id;

                await _context.SaveChangesAsync();
                TempData["MensagemSucesso"] = "Coordenador atribuído com sucesso!";
            } catch (Exception ex) {
                LogService.RegistarErro("AtribuirCoordenadorCurso POST", ex);
                TempData["Erro"] = "Ocorreu um erro no servidor ao tentar guardar a atribuição.";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Remove a vinculação de um coordenador ao respetivo curso.
        /// Avalia as dependências: se o utilizador não coordenar mais nenhum curso no sistema, 
        /// os seus privilégios (Role) de Coordenador Erasmus são revogados por motivos de segurança (Princípio do Menor Privilégio).
        /// </summary>
        /// <param name="cursoId">A chave primária do curso que ficará "órfão" de coordenador.</param>
        /// <returns>Redireciona para o painel principal refletindo a remoção.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoverCoordenadorCurso(int cursoId) {
            if (cursoId <= 0) {
                TempData["Erro"] = "Tem de selecionar um curso.";
                return RedirectToAction(nameof(Index));
            }

            try {
                var curso = await _context.Cursos
                    .Include(c => c.Coordenador)
                    .FirstOrDefaultAsync(c => c.Id == cursoId);

                if (curso == null) {
                    TempData["Erro"] = "O curso selecionado não foi encontrado.";
                    return RedirectToAction(nameof(Index));
                }

                if (string.IsNullOrWhiteSpace(curso.CoordenadorFK)) {
                    TempData["Erro"] = "Este curso não tem coordenador atribuído.";
                    return RedirectToAction(nameof(Index));
                }

                var coordenadorId = curso.CoordenadorFK;
                var coordenador = curso.Coordenador;

                curso.CoordenadorFK = null;
                await _context.SaveChangesAsync();

                if (coordenador != null) {
                    var aindaTemCursos = await _context.Cursos
                        .AnyAsync(c => c.CoordenadorFK == coordenadorId);

                    if (!aindaTemCursos && await _userManager.IsInRoleAsync(coordenador, "CoordenadorErasmus")) { 
                        var resultadoRole = await _userManager.RemoveFromRoleAsync(coordenador, "CoordenadorErasmus");

                        if (!resultadoRole.Succeeded)
                        {
                            TempData["Erro"] = "O coordenador foi removido do curso, mas não foi possível remover a role Coordenador Erasmus.";
                            return RedirectToAction(nameof(Index));
                        }
                    }
                }

                TempData["MensagemSucesso"] = "Coordenador removido do curso com sucesso.";
            } catch (Exception ex) {
                LogService.RegistarErro("RemoverCoordenadorCurso POST", ex);
                TempData["Erro"] = "Ocorreu um erro interno ao remover o coordenador do curso.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: Administration/AtribuirGRI
        /// <summary>
        /// Atribui a role de GRI a um utilizador através do email indicado.
        /// Caso o utilizador ainda não exista, cria uma conta base sem password
        /// e associa-lhe a role de GRI.
        /// </summary>
        /// <param name="griEmail">Email do utilizador a associar à role GRI.</param>
        /// <returns>Redirecionamento para a página principal de administração.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AtribuirGRI(string griEmail) {
            if (string.IsNullOrWhiteSpace(griEmail)) {
                TempData["Erro"] = "Tem de indicar o email do utilizador GRI.";
                return RedirectToAction(nameof(Index));
            }

            try {
                var emailNormalizado = griEmail.Trim();
                var gri = await _userManager.FindByEmailAsync(emailNormalizado);

                if (gri == null) {
                    gri = CriarUtilizadorPorEmail(emailNormalizado);

                    var resultadoCriacao = await _userManager.CreateAsync(gri);

                    if (!resultadoCriacao.Succeeded) {
                        TempData["Erro"] = "Não foi possível criar a conta do utilizador indicado.";
                        return RedirectToAction(nameof(Index));
                    }

                    if (gri is Aluno && !await _userManager.IsInRoleAsync(gri, "Aluno")) {
                        await _userManager.AddToRoleAsync(gri, "Aluno");
                    }
                }
                if (await _userManager.IsInRoleAsync(gri, "GRI"))
                {
                    TempData["Erro"] = "Este utilizador já pertence ao GRI.";
                    return RedirectToAction(nameof(Index));
                }

                var resultadoRole = await _userManager.AddToRoleAsync(gri, "GRI");

                if (!resultadoRole.Succeeded) {
                    TempData["Erro"] = "Não foi possível atribuir o papel de GRI ao utilizador.";
                    return RedirectToAction(nameof(Index));
                }

                TempData["MensagemSucesso"] = "Novo membro do GRI adicionado com sucesso!";
            } catch (Exception ex) {
                LogService.RegistarErro("AtribuirGRI POST", ex);
                TempData["Erro"] = "Ocorreu um erro interno ao processar o novo membro do GRI.";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Remove as permissões de acesso ao Gabinete de Relações Internacionais (Role: GRI) de um utilizador,
        /// impedindo que a conta continue a validar ou rejeitar candidaturas.
        /// </summary>
        /// <param name="griEmail">O email do membro a destituir.</param>
        /// <returns>Redirecionamento com alerta de sucesso ou insucesso.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoverGRI(string griEmail) {
            if (string.IsNullOrWhiteSpace(griEmail)) {
                TempData["Erro"] = "Tem de indicar o email do utilizador GRI.";
                return RedirectToAction(nameof(Index));
            }

            try {
                var gri = await _userManager.FindByEmailAsync(griEmail.Trim());

                if (gri == null) {
                    TempData["Erro"] = "O utilizador indicado não existe.";
                    return RedirectToAction(nameof(Index));
                }

                if (!await _userManager.IsInRoleAsync(gri, "GRI")) {
                    TempData["Erro"] = "Este utilizador não pertence ao GRI.";
                    return RedirectToAction(nameof(Index));
                }

                var resultado = await _userManager.RemoveFromRoleAsync(gri, "GRI");

                if (!resultado.Succeeded) {
                    TempData["Erro"] = "Não foi possível remover a role GRI.";
                    return RedirectToAction(nameof(Index));
                }

                TempData["MensagemSucesso"] = "Membro do GRI removido com sucesso.";
            } catch (Exception ex) {
                LogService.RegistarErro("RemoverGRI POST", ex);
                TempData["Erro"] = "Ocorreu um erro interno ao remover o membro do GRI.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: Administration/InitializeDatabase
        /// <summary>
        /// Ação crítica concebida para arranques em ambientes de Produção ou Disaster Recovery.
        /// Invoca a classe DbInitializer para povoar (Seed) os dados fundamentais estruturais do ecossistema 
        /// (Escolas, Cursos, Países, Universidades Parceiras) contornando a necessidade de scripts SQL manuais.
        /// </summary>
        /// <param name="serviceProvider">Fornecedor de serviços para injetar o contexto e gestor de papéis durante o seed.</param>
        /// <returns>Redireciona confirmando a injeção maciça de dados operacionais.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitializeDatabase([FromServices] IServiceProvider serviceProvider) {
            try {
                var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

                // 1. Semeia tabelas gerais (Escolas, Cursos, Paises, Universidades, etc)
                appErasmus.Data.DbInitializer.Initialize(context);

                // 2. Semeia as Roles e cria a conta do SuperAdmin (O que estava no Program.cs)
                await appErasmus.Data.DbInitializer.SeedRolesAndSuperUserAsync(serviceProvider);

                TempData["MensagemSucesso"] = "Base de Dados inicializada e semeada com sucesso!";
            }
            catch (Exception ex) {
                appErasmus.Services.LogService.RegistarErro("InitializeDatabase Seed POST", ex);
                TempData["Erro"] = "Ocorreu uma falha ao tentar semear a base de dados. Verifica os logs.";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Delegação da lógica de negócio que formata a estrutura de apresentação dos cursos.
        /// Agrupa os dados e prepara a SelectList que alimenta o motor visual (ViewBag) das dropdowns,
        /// associando a chave do curso ao nome atual do seu coordenador.
        /// </summary>
        private async Task CarregarDadosAtribuicaoCoordenadorAsync()
        {
            var cursos = await _context.Cursos
                .Include(c => c.Escola)
                .Include(c => c.GrauCurso)
                .Include(c => c.Coordenador)
                .Where(c => c.IsActive)
                .OrderBy(c => c.Escola.Nome)
                .ThenBy(c => c.NomePTCurso)
                .Select(c => new
                {
                    Id = c.Id,

                    Nome = c.CodigoCurso + " - " + c.NomePTCurso +
                           " (" + c.GrauCurso.Nome + ")",

                    GrupoOrganizado = c.Escola.Nome,

                    // Serve apenas para preencher o campo readonly na view.
                    CoordenadorAtual = c.Coordenador == null
                        ? "Sem coordenador"
                        : c.Coordenador.Email
                })
                .ToListAsync();

            ViewBag.Cursos = new SelectList(
                cursos,
                "Id",
                "Nome",
                null,
                "GrupoOrganizado"
            );

            ViewBag.CoordenadoresAtuais = cursos.ToDictionary(
                c => c.Id.ToString(),
                c => c.CoordenadorAtual
            );
        }

        /// <summary>
        /// Máquina inferencial que avalia o endereço de email fornecido. 
        /// Se o email começar por "aluno" e for seguido de números (padrão institucional IPT),
        /// gera uma entidade <see cref="Aluno"/> isolando o seu número mecanicamento.
        /// Caso contrário, assume tratar-se de Corpo Docente ou Técnico e instancia um <see cref="Utilizador"/> normal.
        /// </summary>
        /// <param name="email">Endereço de correio eletrónico a analisar e instanciar.</param>
        /// <returns>Uma nova instância derivada de Utilizador (Aluno ou Funcionário) pronta a gravar.</returns>
        private Utilizador CriarUtilizadorPorEmail(string email) {
            if (!string.IsNullOrWhiteSpace(email) && email.Trim().ToLower().StartsWith("aluno")) {

                string apenasNumeros = new string(email.Where(char.IsDigit).ToArray());
                int.TryParse(apenasNumeros, out int numeroAlunoExtraido);

                return new Aluno {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    NomeCompleto = email,
                    NumeroAluno = numeroAlunoExtraido,
                    AnoCurricular = 1,
                    IsAtivo = true,
                    DataRegisto = DateTime.Now
                };
            }

            return new Utilizador {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                NomeCompleto = email,
                IsAtivo = true,
                DataRegisto = DateTime.Now
            };
        }

        /// <summary>
        /// Concede acesso total ao painel de Administração a um utilizador específico através do seu email.
        /// Tal como no GRI e Coordenadores, se a conta for inexistente, será alocada de forma implícita.
        /// </summary>
        /// <param name="adminEmail">O email a promover a Administrador do sistema.</param>
        /// <returns>Redireciona com feedback visual de conclusão.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AtribuirAdmin(string adminEmail) {
            if (string.IsNullOrWhiteSpace(adminEmail)) {
                TempData["Erro"] = "Tem de indicar o email do utilizador administrador.";
                return RedirectToAction(nameof(Index));
            }

            try {
                var emailNormalizado = adminEmail.Trim();
                var admin = await _userManager.FindByEmailAsync(emailNormalizado);

                if (admin == null) {
                    admin = CriarUtilizadorPorEmail(emailNormalizado);

                    var resultadoCriacao = await _userManager.CreateAsync(admin);

                    if (!resultadoCriacao.Succeeded) {
                        TempData["Erro"] = "Não foi possível criar a conta do utilizador indicado.";
                        return RedirectToAction(nameof(Index));
                    }

                    if (admin is Aluno && !await _userManager.IsInRoleAsync(admin, "Aluno")) {
                        await _userManager.AddToRoleAsync(admin, "Aluno");
                    }
                }

                if (await _userManager.IsInRoleAsync(admin, "Admin")) {
                    TempData["Erro"] = "Este utilizador já é administrador.";
                    return RedirectToAction(nameof(Index));
                }

                var resultadoRole = await _userManager.AddToRoleAsync(admin, "Admin");

                if (!resultadoRole.Succeeded)
                {
                    TempData["Erro"] = "Não foi possível atribuir a role Admin ao utilizador.";
                    return RedirectToAction(nameof(Index));
                }

                TempData["MensagemSucesso"] = "Administrador atribuído com sucesso!"; 

            } catch (Exception ex) {
                LogService.RegistarErro("AtribuirAdmin POST", ex);
                TempData["Erro"] = "Ocorreu um erro interno ao processar o novo administrador.";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Revoga os privilégios totais de um Administrador.
        /// Incorpora uma trava de segurança crítica: impede a remoção caso o alvo 
        /// seja o último administrador disponível, prevenindo que o sistema fique refém ou inoperacional ("lockout").
        /// </summary>
        /// <param name="adminEmail">Email do administrador a despromover.</param>
        /// <returns>Redirecionamento com o resultado final da operação.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoverAdmin(string adminEmail) {
            if (string.IsNullOrWhiteSpace(adminEmail)) {
                TempData["Erro"] = "Tem de indicar o email do utilizador administrador.";
                return RedirectToAction(nameof(Index));
            }

            try {
                var admin = await _userManager.FindByEmailAsync(adminEmail.Trim());

                if (admin == null) {
                    TempData["Erro"] = "O utilizador indicado não existe.";
                    return RedirectToAction(nameof(Index));
                }

                if (!await _userManager.IsInRoleAsync(admin, "Admin")) {
                    TempData["Erro"] = "Este utilizador não é administrador.";
                    return RedirectToAction(nameof(Index));
                }

                var admins = await _userManager.GetUsersInRoleAsync("Admin");

                if (admins.Count <= 1) {
                    TempData["Erro"] = "Não é possível remover o último administrador do sistema.";
                    return RedirectToAction(nameof(Index));
                }

                var resultado = await _userManager.RemoveFromRoleAsync(admin, "Admin");

                if (!resultado.Succeeded) {
                    TempData["Erro"] = "Não foi possível remover a role Admin.";
                    return RedirectToAction(nameof(Index));
                }

                TempData["MensagemSucesso"] = "Administrador removido com sucesso.";
            } catch (Exception ex) {
                LogService.RegistarErro("RemoverAdmin POST", ex);
                TempData["Erro"] = "Ocorreu um erro interno ao remover o administrador.";
            }

            return RedirectToAction(nameof(Index));
        }
    }

}