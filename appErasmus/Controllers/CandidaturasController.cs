using appErasmus.Data;
using appErasmus.Models;
using appErasmus.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;

namespace appErasmus.Controllers {
    /// <summary>
    /// Controller responsável pela área do aluno no processo de candidatura Erasmus.
    /// Permite criar, editar, submeter, consultar, desistir e gerir documentos das candidaturas.
    /// </summary>
    [Authorize(Roles = "Aluno")]
    public class CandidaturasController : Controller {
        private readonly ApplicationDbContext _context;
        private readonly IServicoEmail _servicoEmail;
        private readonly string _emailGRI;

        /// <summary>
        /// Inicializa o controller com o contexto da base de dados.
        /// </summary>
        /// <param name="context">Contexto do Entity Framework para acesso à base de dados.</param>
        /// <param name="servicoEmail">Serviço de envio de emails transacionais (Notificações).</param>
        /// <param name="configuration">Acesso ao appsettings.json para extração de variáveis de ambiente.</param>
        public CandidaturasController(ApplicationDbContext context, IServicoEmail servicoEmail, IConfiguration configuration) {
        _context = context;
            _servicoEmail = servicoEmail;
            _emailGRI = configuration["ContactosInstitucionais:EmailGRI"] ?? throw new InvalidOperationException("O email do GRI não está configurado em appsettings.json."); 
        }

        /// <summary>
        /// Verifica se o aluno autenticado já tem uma candidatura ativa.
        /// Considera como ativa uma candidatura em rascunho, submetida, em análise ou devolvida para correção.
        /// </summary>
        /// <returns>True se existir uma candidatura ativa; caso contrário, false.</returns>
        private async Task<bool> AlunoTemCandidaturaAtivaAsync() {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId)) return false;

            try {
                return await _context.Candidaturas
                    .AnyAsync(c =>
                        c.AlunoFK == userId &&
                        (
                            c.EstadoCandidatura == EnumEstadoCandidatura.EmConstrucao ||
                            c.EstadoCandidatura == EnumEstadoCandidatura.Submetida ||
                            c.EstadoCandidatura == EnumEstadoCandidatura.EmAnaliseGRI ||
                            c.EstadoCandidatura == EnumEstadoCandidatura.DevolvidaParaCorrecao || 
                            c.EstadoCandidatura == EnumEstadoCandidatura.EmAnaliseFinalGRI
                        )
                    );
            }
            catch (Exception ex) {
                LogService.RegistarErro("BD AlunoTemCandidaturaAtivaAsync", ex);
                return false;
            }
        }

        /// <summary>
        /// Verifica se o perfil académico e pessoal do aluno está completo
        /// antes de permitir o acesso à área de candidaturas.
        /// </summary>
        /// <returns>True se o perfil estiver completo; caso contrário, false.</returns>
        private async Task<bool> IsPerfilAlunoCompletoAsync() {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId)) return false;

            try {
                var aluno = await _context.Users
                    .OfType<Aluno>()
                    .Include(a => a.Cursos)
                    .FirstOrDefaultAsync(a => a.Id == userId);

                if (aluno == null) return false;

                if (aluno.NumeroAluno <= 0) return false;

                if (aluno.AnoCurricular <= 0) return false;

                if (string.IsNullOrWhiteSpace(aluno.NIF)) return false;

                if (string.IsNullOrWhiteSpace(aluno.NumeroDocumento)) return false;

                if (aluno.Cursos == null || !aluno.Cursos.Any()) return false;

                return true;
            } catch (Exception ex) {
                LogService.RegistarErro("BD IsPerfilAlunoCompletoAsync", ex);
                return false;
            }
        }

        /// <summary>
        /// Executa uma validação global antes das actions do controller,
        /// impedindo o acesso à área de candidaturas quando o perfil do aluno está incompleto.
        /// </summary>
        /// <param name="context">Contexto da execução da action.</param>
        /// <param name="next">Delegado que continua a execução da action.</param>
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) {
            var perfilCompleto = await IsPerfilAlunoCompletoAsync();

            if (!perfilCompleto) {
                TempData["FirstLogin"] = "true";
                context.Result = RedirectToPage("/Account/Manage/Index", new { area = "Identity" });
                return;
            }

            await next();
        }

        // GET: Candidaturas
        /// <summary>
        /// Lista as candidaturas pertencentes ao aluno autenticado.
        /// Também indica se o aluno pode criar uma nova candidatura.
        /// </summary>
        /// <returns>View com a lista de candidaturas do aluno.</returns>
        public async Task<IActionResult> Index() {
            // Obtém o identificador único (ID) do utilizador atualmente logado na sessão
            var userIdOficial = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var minhasCandidaturas = new List<Candidatura>();

            try {
                minhasCandidaturas = await _context.Candidaturas
                    .Where(c => c.AlunoFK == userIdOficial)
                    .OrderByDescending(c => c.DataSubmissao)
                    .ToListAsync();
            } catch (Exception ex) {
                LogService.RegistarErro("Index GET", ex);
                TempData["Erro"] = "Não foi possível carregar a listagem de candidaturas.";
            }

            ViewBag.CanCreateNewCandidatura = !await AlunoTemCandidaturaAtivaAsync();
            return View(minhasCandidaturas);
        }

        // GET: Candidaturas/Details/5
        /// <summary>
        /// Mostra os detalhes de uma candidatura do aluno autenticado,
        /// incluindo curso, línguas, universidades escolhidas e documentos anexados.
        /// </summary>
        /// <param name="id">Identificador da candidatura.</param>
        /// <returns>View de detalhe da candidatura ou erro de acesso quando aplicável.</returns>
        public async Task<IActionResult> Details(Guid? id) {
            if (id == null) {
                TempData["Erro"] = "ID do processo em falta.";
                return RedirectToAction("AcessoRestrito", "Home");
            }

            try {
                var candidatura = await _context.Candidaturas
                        .Include(c => c.Curso)
                            .ThenInclude(curso => curso.Escola)
                        .Include(c => c.Curso)
                            .ThenInclude(curso => curso.GrauCurso)
                        .Include(c => c.Linguas)
                        .Include(c => c.Documentos)
                            .ThenInclude(d => d.Categoria)
                        .Include(c => c.CandidaturasUniversidades)
                            .ThenInclude(cu => cu.Universidade)
                        .FirstOrDefaultAsync(m => m.Id == id);

                // Se a candidatura não existir (Antigo NotFound)
                if (candidatura == null) {
                    TempData["Erro"] = "A candidatura pretendida não foi encontrada.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var userIdOficial = User.FindFirstValue(ClaimTypes.NameIdentifier);

                // CONTROLO DE ACESSO: Se o utilizador não for do GRI nem Coordenador, valida se é o dono.
                // Impede que um aluno cusque ou aceda a dados de outros alterando manualmente o ID no URL.
                if (candidatura.AlunoFK != userIdOficial) {
                    TempData["Erro"] = "Acesso recusado. Não tens permissão para visualizar este processo.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                return View(candidatura);
            } catch (Exception ex) {
                LogService.RegistarErro("Details GET Aluno", ex);
                TempData["Erro"] = "Erro interno ao consultar o processo.";
                return RedirectToAction("AcessoRestrito", "Home");
            }
        }

        // GET: Candidaturas/Create
        /// <summary>
        /// Mostra o formulário de criação de uma nova candidatura.
        /// Carrega os dados do perfil do aluno, anos letivos, universidades e categorias de documentos.
        /// </summary>
        /// <returns>View de criação de candidatura.</returns>
        public async Task<IActionResult> Create() {
            if (await AlunoTemCandidaturaAtivaAsync()) {
                return RedirectToAction(nameof(Index));
            }

            try {
                await CarregarDadosPerfilAlunoAsync();

                var novaCandidatura = new CandidaturaFormViewModel            {
                    Linguas = new List<CandidaturaLingua>() // Garante que a lista não é nula
                };

                GerarAnosLetivosDinamicos();

                // Procura e estrutura a listagem de universidades parceiras organizadas por tipo de acordo e país
                var listaUniversidades = _context.Universidades
                    .OrderBy(u => u.TipoAcordo)
                    .ThenBy(u => u.Pais)
                    .ThenBy(u => u.Nome)
                    .Select(u => new {
                        ErasmusCode = u.ErasmusCode,
                        Nome = u.Nome,
                        // Cria o rótulo de agrupamento que o Select2 usa para dividir visualmente o ecrã
                        GrupoOrganizado = u.TipoAcordo == "Acordos Bilaterais Erasmus+"
                            ? $"Erasmus+ ({u.Pais})"
                            : $"Internacional ({u.Pais})"
                    })
                    .ToList();

                // Encapsula os dados num SelectList para agrupamento dinâmico ("GrupoOrganizado")
                ViewBag.Universidades = new SelectList(listaUniversidades, "ErasmusCode", "Nome", null, "GrupoOrganizado");

                await CarregarCategoriasDocumentosAsync();
                CarregarCodigosOutraUniversidade();

                return View(novaCandidatura);
            } catch (Exception ex) {
                LogService.RegistarErro("Create GET", ex);
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Carrega os dados do perfil do aluno autenticado para preencher a área informativa
        /// e a lista de cursos disponíveis no formulário de candidatura.
        /// </summary>
        /// <param name="cursoSelecionadoId">Curso atualmente selecionado, quando aplicável.</param>
        private async Task CarregarDadosPerfilAlunoAsync(int? cursoSelecionadoId = null) {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            try {
                var aluno = await _context.Users
                    .OfType<Aluno>()
                    .Include(a => a.Cursos)
                        .ThenInclude(c => c.Escola)
                    .Include(a => a.Cursos)
                        .ThenInclude(c => c.GrauCurso)
                    .FirstOrDefaultAsync(a => a.Id == userId);

                if (aluno == null) {
                    ViewBag.PerfilAlunoEncontrado = false;
                    return;
                }

                var cursosAluno = aluno.Cursos
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.Escola.Nome)
                    .ThenBy(c => c.NomePTCurso)
                    .Select(c => new {
                        Id = c.Id,
                        Nome = c.CodigoCurso + " - " + c.NomePTCurso,
                        Escola = c.Escola.Nome,
                        Grau = c.GrauCurso.Nome,
                        Selected = cursoSelecionadoId.HasValue && c.Id == cursoSelecionadoId.Value
                    })
                    .ToList();

                ViewBag.PerfilAlunoEncontrado = true;
                ViewBag.PerfilNomeCompleto = aluno.NomeCompleto;
                ViewBag.PerfilEmail = aluno.Email;
                ViewBag.PerfilNumeroAluno = aluno.NumeroAluno;
                ViewBag.PerfilAnoCurricular = aluno.AnoCurricular;
                ViewBag.PerfilNIF = aluno.NIF;
                ViewBag.PerfilTipoDocumento = aluno.TipoDocumento.ToString();
                ViewBag.PerfilNumeroDocumento = aluno.NumeroDocumento;

                ViewBag.CursosAluno = cursosAluno;
                ViewBag.TotalCursosAluno = cursosAluno.Count;
            }
            catch (Exception ex) {
                LogService.RegistarErro("CarregarDadosPerfilAlunoAsync", ex);
                ViewBag.PerfilAlunoEncontrado = false;
            }
        }

        // POST: Candidaturas/Create
        /// <summary>
        /// Cria uma nova candidatura do aluno, podendo guardá-la como rascunho
        /// ou submetê-la oficialmente. Também valida universidades, línguas, curso e documentos anexados.
        /// </summary>
        /// <param name="model">O ViewModel (DTO) populado via Model Binding.</param>
        /// <param name="universidade1">Código Erasmus da 1ª escolha institucional.</param>
        /// <param name="universidade2">Código Erasmus da 2ª escolha institucional.</param>
        /// <param name="universidade3">Código Erasmus da 3ª escolha institucional.</param>
        /// <param name="categoriasDocumento">Arrays mapeados das classificações de anexos.</param>
        /// <param name="descricoesDocumento">Textos descritivos em caso de seleção de categoria 'Outro'.</param>
        /// <param name="ficheirosDocumento">Streams binárias enviadas ao servidor (Payloads).</param>
        /// <param name="universidade1Other">Designação textual manual caso a 1ª opção não exista no catálogo.</param>
        /// <param name="universidade2Other">Designação textual manual caso a 2ª opção não exista no catálogo.</param>
        /// <param name="universidade3Other">Designação textual manual caso a 3ª opção não exista no catálogo.</param>
        /// <param name="botaoAcao">Sinaliza a intent do aluno ('rascunho' ou 'submeter').</param>
        /// <returns>Redireciona para o Index em caso de sucesso, ou reconstrói o formulário em caso de falha de validação.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CandidaturaFormViewModel model,
            string? universidade1, string? universidade2, string? universidade3,
            List<string> categoriasDocumento, List<string?> descricoesDocumento, List<IFormFile> ficheirosDocumento,
            string? universidade1Other, string? universidade2Other, string? universidade3Other,
            string botaoAcao) {

            if (await AlunoTemCandidaturaAtivaAsync()) {
                return RedirectToAction(nameof(Index));
            }

            try {
                categoriasDocumento ??= [];
                descricoesDocumento ??= [];
                ficheirosDocumento ??= [];

                await ValidarDocumentosAsync(categoriasDocumento, descricoesDocumento, ficheirosDocumento);

                // Remoção de linhas vazias
                model.Linguas = model.Linguas?
                    .Where(l => !string.IsNullOrWhiteSpace(l.Lingua))
                    .ToList() ?? [];

                // 2. VALIDAÇÃO DE LÍNGUAS REPETIDAS (Camada de Segurança do Servidor)
                if (model.Linguas != null && model.Linguas.Any()) {
                    bool temLinguasRepetidas = model.Linguas
                        .Where(l => !string.IsNullOrWhiteSpace(l.Lingua))
                        .GroupBy(l => l.Lingua.Trim().ToLower()) // Normaliza o texto para capturar variações de caixa
                        .Any(g => g.Count() > 1);

                    if (temLinguasRepetidas)
                        ModelState.AddModelError(string.Empty, "Não podes adicionar a mesma língua mais do que uma vez.");
                }

                // 3. VALIDAÇÃO DE UNIVERSIDADES SELECIONADAS
                ValidarUniversidadesEscolhidas(universidade1, universidade2, universidade3, universidade1Other, universidade2Other, universidade3Other);

                if (!AnoLetivoValido(model.AnoLetivo)) {
                    ModelState.AddModelError(nameof(model.AnoLetivo), "O ano letivo selecionado é inválido.");
                }

                if (!SemestreValido(model.Semestre)) {
                    ModelState.AddModelError(nameof(model.Semestre), "O semestre selecionado é inválido.");
                }

                var userIdOficial = User.FindFirstValue(ClaimTypes.NameIdentifier);
             
                bool cursoPertenceAoAluno = false;

                if (model.CursoFK.HasValue) {
                    cursoPertenceAoAluno = await _context.Users
                        .OfType<Aluno>()
                        .AnyAsync(a =>
                            a.Id == userIdOficial &&
                            a.Cursos.Any(c =>
                                c.Id == model.CursoFK.Value &&
                                c.IsActive));
                }
                if (!cursoPertenceAoAluno) {
                    ModelState.AddModelError(nameof(model.CursoFK), "O curso selecionado não pertence ao teu perfil.");
                }
                // 4. CONSTRUÇÃO DO OBJETO FINAL E PERSISTÊNCIA NA BASE DE DADOS
                if (ModelState.IsValid) {

                    model.IBAN = model.IBAN?.Replace(" ", "").ToUpper();

                    var candidatura = new Candidatura {
                        AlunoFK = userIdOficial!,
                        AnoLetivo = model.AnoLetivo!,
                        Semestre = model.Semestre!.Value,
                        Bolseiro = model.Bolseiro,
                        IBAN = model.IBAN!,
                        CursoFK = model.CursoFK!.Value,
                        DataSubmissao = DateTime.Now,
                        Linguas = model.Linguas ?? [],
                        CandidaturasUniversidades = ConstruirCandidaturasUniversidades(universidade1, universidade2, universidade3, universidade1Other, universidade2Other, universidade3Other)
                    }; 

                    // MÁQUINA DE ESTADOS: Avalia qual o botão clicado na interface do utilizador
                    if (botaoAcao == "rascunho") {
                        candidatura.EstadoCandidatura = EnumEstadoCandidatura.EmConstrucao;
                    } else {
                        candidatura.EstadoCandidatura = EnumEstadoCandidatura.Submetida;
                    } 

                    // Adiciona a entidade raiz. O EF Core infere as tabelas filhas automaticamente.
                    _context.Add(candidatura);
                    await _context.SaveChangesAsync();

                        categoriasDocumento ??= new List<string>();
                        descricoesDocumento ??= new List<string?>();
                        ficheirosDocumento ??= new List<IFormFile>();

                        for (int i = 0; i < ficheirosDocumento.Count; i++) {
                            var ficheiro = ficheirosDocumento[i];
                            if (ficheiro == null || ficheiro.Length == 0) continue;

                            var categoria = i < categoriasDocumento.Count ? categoriasDocumento[i] : string.Empty;
                            var descricao = i < descricoesDocumento.Count ? descricoesDocumento[i] : string.Empty;

                            await GuardarDocumentoAsync(candidatura.Id, userIdOficial!, categoria, descricao, ficheiro);
                        }

                        await _context.SaveChangesAsync();

                        //  DISPARAR EMAIL SE FOR SUBMISSÃO DEFINITIVA 
                        if (botaoAcao != "rascunho") {
                            try {
                                // 1. O email de segurança (fallback). 
                                // Se o curso ainda não tiver coordenador atribuído, vai para este email para não se perder.
                                string emailCoordenador = _emailGRI;

                                // 2. PROCURA DINÂMICA DO COORDENADOR
                                var cursoComCoordenador = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == candidatura.CursoFK);

                                if (cursoComCoordenador != null && !string.IsNullOrWhiteSpace(cursoComCoordenador.CoordenadorFK)) {
                                    var coordenadorUser = await _context.Users.FindAsync(cursoComCoordenador.CoordenadorFK);
                                    if (coordenadorUser != null && !string.IsNullOrWhiteSpace(coordenadorUser.Email)) {
                                        emailCoordenador = coordenadorUser.Email;
                                    }
                                }

                                // 3. Gerar o Link Direto
                                var linkDireto = Url.Action("Details", "Coordenator", new { id = candidatura.Id }, protocol: Request.Scheme) ?? string.Empty;

                            // 4. Obter os dados do aluno para o corpo do email
                            var aluno = await _context.Users.OfType<Aluno>().FirstOrDefaultAsync(a => a.Id == candidatura.AlunoFK);
                                string nomeAluno = aluno != null ? aluno.NomeCompleto : "Aluno";
                                string numeroAluno = aluno != null ? aluno.NumeroAluno.ToString() : "N/A";

                                // 5. Construir o Email
                                string assunto = $"[Portal Erasmus] Nova Candidatura Pendente - {nomeAluno}";
                                string mensagemHtml = $@"
                                    <div style='font-family: sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #eee; padding: 20px; border-radius: 8px;'>
                                        <h2 style='color: #0d6efd;'>Nova Candidatura Submetida</h2>
                                        <p>Olá, Coordenador(a),</p>
                                        <p>Informamos que o aluno <strong>{nomeAluno}</strong> ({numeroAluno}) submeteu a sua candidatura de mobilidade Erasmus para o ano letivo <strong>{candidatura.AnoLetivo}</strong>.</p>
                                        <p>O processo encontra-se a aguardar a sua avaliação e validação académica.</p>
                                        <div style='margin: 30px 0; text-align: center;'>
                                            <a href='{linkDireto}' style='background-color: #0d6efd; color: white; padding: 12px 24px; text-decoration: none; font-weight: bold; border-radius: 5px; display: inline-block;'>
                                                Avaliar Candidatura
                                            </a>
                                        </div>
                                        <hr style='border: 0; border-top: 1px solid #eee;' />
                                        <p style='font-size: 0.8rem; color: #777;'>Este é um email automático do Portal Erasmus@IPT.</p>
                                    </div>";

                                // 6. Enviar em background (Task.Run) para não bloquear o ecrã do aluno
                                _ = Task.Run(async () => await _servicoEmail.EnviarEmailAsync(emailCoordenador, assunto, mensagemHtml));
                            }
                            catch (Exception ex) {
                                LogService.RegistarErro("Lógica Email Aluno Create", ex);
                            }
                        }
                    //  FIM DO BLOCO DO EMAIL 

                    return RedirectToAction(nameof(Index));
                }
            } catch (Exception ex) {
                LogService.RegistarErro("Create POST", ex);
                ModelState.AddModelError(string.Empty, "Ocorreu um erro crítico de persistência na Base de Dados ao salvar a candidatura.");
            }

            await CarregarDadosPerfilAlunoAsync(model.CursoFK);

            // 5. PLANO DE RECUO: Se houver erros de validação, reconstrói as dropdowns antes de recarregar a View
            ViewBag.Universidades = new SelectList(ObterListaUniversidadesFormatada(), "ErasmusCode", "Nome", null, "GrupoOrganizado");
            GerarAnosLetivosDinamicos();
            await CarregarCategoriasDocumentosAsync();
            RepovoarListasApoio();

            return View(model);
        }

        // GET: Candidaturas/Edit/5
        /// <summary>
        /// Mostra o formulário de edição de uma candidatura ainda em rascunho
        /// ou devolvida para correção.
        /// </summary>
        /// <param name="id">Identificador da candidatura.</param>
        /// <returns>View de edição ou erro de acesso quando aplicável.</returns>
        public async Task<IActionResult> Edit(Guid? id) {
            if (id == null) {
                TempData["Erro"] = "ID do processo nulo.";
                return RedirectToAction("AcessoRestrito", "Home");
            }

            try {
                var candidatura = await _context.Candidaturas
                    .Include(c => c.Linguas)
                    .Include(c => c.Documentos)
                        .ThenInclude(d => d.Categoria)
                    .Include(c => c.CandidaturasUniversidades)
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (candidatura == null) {
                    TempData["Erro"] = "Candidatura não localizada.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var userIdOficial = User.FindFirstValue(ClaimTypes.NameIdentifier);

                // SEGURANÇA: Bloqueia tentativas de acesso cruzado entre alunos distintos
                if (candidatura.AlunoFK != userIdOficial) {
                    TempData["Erro"] = "Acesso Negado. Não és o proprietário deste rascunho.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (await RequerCorrecaoDocumentosFinaisAsync(candidatura.Id)) {
                    TempData["Erro"] = "Nesta fase apenas podes corrigir os documentos finais solicitados pelo GRI.";
                    return RedirectToAction(nameof(Details), new { id = candidatura.Id });
                }
                // Só é permitido aceder ao ecrã de edição se o registo for um Rascunho privado ou for devolvido para correção.
                if (candidatura.EstadoCandidatura != EnumEstadoCandidatura.EmConstrucao && candidatura.EstadoCandidatura != EnumEstadoCandidatura.DevolvidaParaCorrecao) {
                    TempData["Erro"] = "Esta candidatura não pode ser alterada.";
                    return RedirectToAction(nameof(Index));
                }

                // Extrai as escolhas guardadas para preencher e injetar o estado atual correto nos campos do Select2
                var escolhasAtuais = candidatura.CandidaturasUniversidades.OrderBy(u => u.Preferencia).ToList();
                ViewBag.Uni1Selecionada = escolhasAtuais.FirstOrDefault(u => u.Preferencia == 1)?.UniversidadeFK;
                ViewBag.Uni2Selecionada = escolhasAtuais.FirstOrDefault(u => u.Preferencia == 2)?.UniversidadeFK;
                ViewBag.Uni3Selecionada = escolhasAtuais.FirstOrDefault(u => u.Preferencia == 3)?.UniversidadeFK;

                await CarregarDadosPerfilAlunoAsync(candidatura.CursoFK);
                RepovoarListasApoio();
                await CarregarCategoriasDocumentosAsync();

                var model = new CandidaturaFormViewModel
                {
                    Id = candidatura.Id,
                    AnoLetivo = candidatura.AnoLetivo,
                    Semestre = candidatura.Semestre,
                    Bolseiro = candidatura.Bolseiro,
                    IBAN = candidatura.IBAN,
                    CursoFK = candidatura.CursoFK,
                    Linguas = candidatura.Linguas?.ToList() ?? [],
                    Documentos = candidatura.Documentos?.ToList() ?? [],
                    CandidaturasUniversidades = candidatura.CandidaturasUniversidades?.ToList() ?? [],
                    EstadoCandidatura = candidatura.EstadoCandidatura,
                    OrigemCorrecao = candidatura.OrigemCorrecao,
                    MotivoRejeicao = candidatura.MotivoRejeicao
                };

                return View(model); 
            } catch (Exception ex) {
                LogService.RegistarErro("Edit GET", ex);
                TempData["Erro"] = "Erro interno ao abrir o editor.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Candidaturas/Edit/5
        /// <summary>
        /// Atualiza uma candidatura editável do aluno, substituindo os dados principais,
        /// línguas e universidades escolhidas. Pode manter a candidatura em rascunho
        /// ou submetê-la novamente.
        /// </summary>
        /// <param name="id">GUID da candidatura na base de dados.</param>
        /// <param name="model">O View Model transportado desde a UI com validações embutidas.</param>
        /// <param name="universidade1">Opção primária preenchida.</param>
        /// <param name="universidade2">Opção secundária preenchida.</param>
        /// <param name="universidade3">Opção terciária preenchida.</param>
        /// <param name="universidade1Other">Designação da instituição alternativa - se aplicável.</param>
        /// <param name="universidade2Other">Designação da instituição alternativa - se aplicável.</param>
        /// <param name="universidade3Other">Designação da instituição alternativa - se aplicável.</param>
        /// <param name="botaoAcao">Instrução contextual (guardar em rascunho vs submeter para análise).</param>
        /// <returns>Encaminhamento limpo perante o estado ou view carregada com ModelState Errors perante falha.</returns> 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, CandidaturaFormViewModel model,
            string? universidade1, string? universidade2, string? universidade3,
            string? universidade1Other, string? universidade2Other, string? universidade3Other,
            string botaoAcao) {

            if (model.Id == null || id != model.Id.Value) {
                TempData["Erro"] = "Incoerência transacional de IDs.";
                return RedirectToAction("AcessoRestrito", "Home");
            }

            Candidatura? candidaturaOriginal = null;

            // Procura o registo original atualmente guardado na base de dados (registo trackeado)
            try
            {
                candidaturaOriginal = await _context.Candidaturas
                    .Include(c => c.CandidaturasUniversidades)
                    .Include(c => c.Linguas)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (candidaturaOriginal == null) {
                    TempData["Erro"] = "Candidatura não localizada.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var userIdOficial = User.FindFirstValue(ClaimTypes.NameIdentifier);
                bool cursoPertenceAoAluno = false;

                if (model.CursoFK.HasValue) {
                    cursoPertenceAoAluno = await _context.Users
                        .OfType<Aluno>()
                        .AnyAsync(a =>
                            a.Id == userIdOficial &&
                            a.Cursos.Any(c =>
                                c.Id == model.CursoFK.Value &&
                                c.IsActive));
                }

                // Verificações redundantes de segurança ao nível do Servidor (Evita adulterações via Postman/Fiddler)
                if (!cursoPertenceAoAluno) {
                    ModelState.AddModelError(nameof(model.CursoFK), "O curso selecionado não pertence ao teu perfil.");
                }

                if (candidaturaOriginal.AlunoFK != userIdOficial) {
                    TempData["Erro"] = "Violação de propriedade de dados.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (await RequerCorrecaoDocumentosFinaisAsync(candidaturaOriginal.Id)) {
                    TempData["Erro"] = "Nesta fase apenas podes corrigir os documentos finais solicitados pelo GRI.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaOriginal.Id });
                }

                if (candidaturaOriginal.EstadoCandidatura != EnumEstadoCandidatura.EmConstrucao && candidaturaOriginal.EstadoCandidatura != EnumEstadoCandidatura.DevolvidaParaCorrecao) {
                        TempData["Erro"] = "Esta candidatura já não se encontra em estado de modificação.";
                        return RedirectToAction("AcessoRestrito", "Home");
                    }

                if (candidaturaOriginal.EstadoCandidatura != EnumEstadoCandidatura.EmConstrucao && candidaturaOriginal.EstadoCandidatura != EnumEstadoCandidatura.DevolvidaParaCorrecao) {
                    return RedirectToAction("Index", "Home");
                }
    
                model.Linguas = model.Linguas?
                    .Where(l => !string.IsNullOrWhiteSpace(l.Lingua))
                    .ToList() ?? new List<CandidaturaLingua>();

                // Validação de repetição de línguas
                if (model.Linguas != null && model.Linguas.Any()) {
                    bool temLinguasRepetidas = model.Linguas
                        .Where(l => !string.IsNullOrWhiteSpace(l.Lingua))
                        .GroupBy(l => l.Lingua.Trim().ToLower())
                        .Any(g => g.Count() > 1);

                        if (temLinguasRepetidas) {
                            ModelState.AddModelError(string.Empty, "Não podes adicionar a mesma língua mais do que uma vez.");
                        }
                    }

                // Validação de repetição de universidades
                ValidarUniversidadesEscolhidas(universidade1, universidade2, universidade3, universidade1Other, universidade2Other, universidade3Other);

                if (!AnoLetivoValido(model.AnoLetivo)) {
                    ModelState.AddModelError(nameof(model.AnoLetivo), "O ano letivo selecionado é inválido.");
                }

                if (!SemestreValido(model.Semestre)) {
                    ModelState.AddModelError(nameof(model.Semestre), "O semestre selecionado é inválido.");
                }

                if (ModelState.IsValid) {

                    model.IBAN = model.IBAN?.Replace(" ", "").ToUpper();

                    // 1. Atualização dos metadados básicos no registo original trackeado pelo Contexto
                    candidaturaOriginal.AnoLetivo = model.AnoLetivo!;
                    candidaturaOriginal.Semestre = model.Semestre!.Value;
                    candidaturaOriginal.Bolseiro = model.Bolseiro;
                    candidaturaOriginal.IBAN = model.IBAN!;
                    candidaturaOriginal.CursoFK = model.CursoFK!.Value;
                    candidaturaOriginal.DataSubmissao = DateTime.Now;

                    // 2. Atualização do Estado baseado no botão de fecho utilizado (Rascunho vs Submissão Oficial)
                    if (botaoAcao == "rascunho") {
                        candidaturaOriginal.EstadoCandidatura = EnumEstadoCandidatura.EmConstrucao;
                    } else {
                        if (candidaturaOriginal.OrigemCorrecao == EnumOrigemCorrecao.GRI) {
                            candidaturaOriginal.EstadoCandidatura = EnumEstadoCandidatura.EmAnaliseGRI;
                        }
                        else {
                            candidaturaOriginal.EstadoCandidatura = EnumEstadoCandidatura.Submetida;
                        }

                        candidaturaOriginal.PermiteCorrecao = false;
                        candidaturaOriginal.OrigemCorrecao = EnumOrigemCorrecao.None;
                    }

                    // 3. ESTRATÉGIA DE SUBSTITUIÇÃO DE COLEÇÕES (Línguas)
                    // Remove fisicamente os registos dependentes antigos e injeta a nova coleção submetida no formulário
                    if (candidaturaOriginal.Linguas != null && candidaturaOriginal.Linguas.Count != 0) {
                        _context.RemoveRange(candidaturaOriginal.Linguas);
                    }
                    candidaturaOriginal.Linguas = model.Linguas ?? new List<CandidaturaLingua>();

                        // 4. ESTRATÉGIA DE SUBSTITUIÇÃO DE RELAÇÕES N:M (Universidades de Destino)
                        // Limpa as escolhas prévias da tabela de ligação e reconstrói as novas preferências
                        if (candidaturaOriginal.CandidaturasUniversidades != null && candidaturaOriginal.CandidaturasUniversidades.Count != 0) {
                            _context.RemoveRange(candidaturaOriginal.CandidaturasUniversidades);
                        }
                        candidaturaOriginal.CandidaturasUniversidades = ConstruirCandidaturasUniversidades(
                            universidade1, universidade2, universidade3,
                            universidade1Other, universidade2Other, universidade3Other);

                        // Grava as alterações de forma transacional e unificada
                        _context.Update(candidaturaOriginal);
                        await _context.SaveChangesAsync();

                        // INÍCIO DO BLOCO DE EMAIL - RESSUBMISSÃO 
                        if (botaoAcao != "rascunho") {
                            try {
                                // Usamos uma lista para acomodar tanto o envio singular (Coordenador) como o envio em lote (GRI)
                                var emailsDestino = new List<string>();
                                string linkDireto = string.Empty;
                                string assunto = string.Empty;
                                string textoCorpo = string.Empty;

                                // Obter os dados do aluno de forma segura
                                var aluno = await _context.Users.OfType<Aluno>().FirstOrDefaultAsync(a => a.Id == candidaturaOriginal.AlunoFK);
                                string nomeAluno = aluno?.NomeCompleto ?? "Aluno";
                                string numeroAluno = aluno?.NumeroAluno.ToString() ?? "N/A";

                                // MÁQUINA DE ROTEAMENTO DE EMAILS: Para quem vai a notificação?
                                if (candidaturaOriginal.EstadoCandidatura == EnumEstadoCandidatura.EmAnaliseGRI) {
                                    // Foi o GRI que devolveu, logo vai para todos os utilizadores com a Role GRI
                                    var roleGriId = await _context.Roles.Where(r => r.Name == "GRI").Select(r => r.Id).FirstOrDefaultAsync();
                                    var emailsGRI = await _context.UserRoles
                                        .Where(ur => ur.RoleId == roleGriId)
                                        .Join(_context.Users, ur => ur.UserId, u => u.Id, (ur, u) => u.Email)
                                        .Where(e => !string.IsNullOrEmpty(e))
                                        .ToListAsync();

                                    if (emailsGRI.Any()) {
                                        emailsDestino.AddRange(emailsGRI!);
                                    }
                                    else {
                                        emailsDestino.Add(_emailGRI);
                                    }

                                    linkDireto = Url.Action("Details", "Coordenator", new { id = candidaturaOriginal.Id }, protocol: Request.Scheme) ?? string.Empty;

                                assunto = $"[Portal Erasmus] Candidatura Corrigida e Ressubmetida - {nomeAluno}";
                                    textoCorpo = $"O aluno <strong>{nomeAluno}</strong> ({numeroAluno}) efetuou as correções solicitadas aos documentos/dados e voltou a submeter a sua candidatura. O processo encontra-se novamente na sua lista para validação regulamentar.";
                                }
                                else if (candidaturaOriginal.EstadoCandidatura == EnumEstadoCandidatura.Submetida) {
                                // Foi o Coordenador que devolveu, logo a correção volta ao Coordenador Dinâmico do Curso
                                string emailCoordenadorTarget = _emailGRI; // Fallback caso falhe

                                    var cursoComCoordenador = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == candidaturaOriginal.CursoFK);
                                    if (cursoComCoordenador != null && !string.IsNullOrWhiteSpace(cursoComCoordenador.CoordenadorFK)) {
                                        var coordenadorUser = await _context.Users.FindAsync(cursoComCoordenador.CoordenadorFK);
                                        if (coordenadorUser != null && !string.IsNullOrWhiteSpace(coordenadorUser.Email)) {
                                        emailCoordenadorTarget = coordenadorUser.Email; // Assume email real
                                        }
                                    }
                                    emailsDestino.Add(emailCoordenadorTarget);

                                    linkDireto = Url.Action("Details", "Coordenator", new { id = candidaturaOriginal.Id }, protocol: Request.Scheme) ?? string.Empty;
                                    assunto = $"[Portal Erasmus] Candidatura Corrigida e Ressubmetida - {nomeAluno}";
                                    textoCorpo = $"O aluno <strong>{nomeAluno}</strong> ({numeroAluno}) efetuou as correções solicitadas e voltou a submeter a sua candidatura para sua reavaliação académica.";
                                }

                                // DISPARO DE EMAILS
                                if (emailsDestino.Any() && !string.IsNullOrEmpty(assunto)) {
                                    string mensagemHtml = $@"
                                        <div style='font-family: sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #eee; padding: 20px; border-radius: 8px;'>
                                            <h2 style='color: #0d6efd;'>Candidatura Ressubmetida</h2>
                                            <p>Olá,</p>
                                            <p>{textoCorpo}</p>
                                            <div style='margin: 30px 0; text-align: center;'>
                                                <a href='{linkDireto}' style='background-color: #0d6efd; color: white; padding: 12px 24px; text-decoration: none; font-weight: bold; border-radius: 5px; display: inline-block;'>
                                                    Avaliar Candidatura
                                                </a>
                                            </div>
                                            <hr style='border: 0; border-top: 1px solid #eee;' />
                                            <p style='font-size: 0.8rem; color: #777;'>Este é um email automático gerado pelo Portal Erasmus@IPT.</p>
                                        </div>";

                                    foreach (var email in emailsDestino) {
                                        string targetEmail = email ?? string.Empty;
                                        if (!string.IsNullOrEmpty(targetEmail)) {
                                            _ = Task.Run(async () => await _servicoEmail.EnviarEmailAsync(targetEmail, assunto, mensagemHtml));
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) {
                                LogService.RegistarErro("Lógica Email Aluno Edit", ex);
                            }
                        }
                        // FIM DO BLOCO DE EMAIL - RESSUBMISSÃO

                        return RedirectToAction(nameof(Index));
                }
            } catch (Exception ex) {
                LogService.RegistarErro("Edit POST", ex);
                ModelState.AddModelError(string.Empty, "Ocorreu um erro crítico de persistência na Base de Dados ao tentar atualizar a candidatura.");
            }

            // Repovoamento em caso de falha nas validações para não quebrar a interface gráfica
            ViewBag.Uni1Selecionada = universidade1;
            ViewBag.Uni2Selecionada = universidade2;
            ViewBag.Uni3Selecionada = universidade3;

            try {
                await CarregarDadosPerfilAlunoAsync(model.CursoFK);
                RepovoarListasApoio();
                await CarregarCategoriasDocumentosAsync();

                model.Documentos = await _context.Documentos
                    .Include(d => d.Categoria)
                    .Where(d => d.CandidaturaFK == id)
                    .OrderByDescending(d => d.DataCriacao)
                    .ToListAsync(); 
            }
            catch (Exception ex) {
                LogService.RegistarErro("Edit POST Fallback", ex);
            }

            if (candidaturaOriginal != null) {
                model.CandidaturasUniversidades = candidaturaOriginal.CandidaturasUniversidades?.ToList()
                    ?? new List<CandidaturaUniversidade>();

                model.EstadoCandidatura = candidaturaOriginal.EstadoCandidatura;
                model.OrigemCorrecao = candidaturaOriginal.OrigemCorrecao;
                model.MotivoRejeicao = candidaturaOriginal.MotivoRejeicao;
            }

            return View(model);
        }

        // GET: Candidaturas/Delete/5
        /// <summary>
        /// Mostra a página de confirmação para desistência ou eliminação de uma candidatura do aluno.
        /// </summary>
        /// <param name="id">Identificador da candidatura.</param>
        /// <returns>View de confirmação ou erro de acesso quando aplicável.</returns>
        public async Task<IActionResult> Delete(Guid? id) {
            if (id == null) {
                TempData["Erro"] = "ID nulo.";
                return RedirectToAction("AcessoRestrito", "Home");
            }

            try {
                var candidatura = await _context.Candidaturas
                    .Include(c => c.Aluno)
                    .Include(c => c.CandidaturasUniversidades)
                        .ThenInclude(cu => cu.Universidade)
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (candidatura == null) {
                    TempData["Erro"] = "Processo não localizado.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

            var userIdOficial = User.FindFirstValue(ClaimTypes.NameIdentifier); 

                if (candidatura.AlunoFK != userIdOficial) {
                    TempData["Erro"] = "Acesso negado. Não és o titular desta candidatura.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                // Impede a redundância de tentar desistir de processos já encerrados ou já cancelados
                if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.Finalizado ||
                    candidatura.EstadoCandidatura == EnumEstadoCandidatura.Desistencia) {

                    TempData["Erro"] = "Esta candidatura já se encontra finalizada ou cancelada.";
                    return RedirectToAction(nameof(Index));
                }

                return View(candidatura);
            }
            catch (Exception ex) {
                LogService.RegistarErro("Delete GET", ex);
                TempData["Erro"] = "Erro interno ao processar o pedido de cancelamento.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Candidaturas/Delete/5
        /// <summary>
        /// Executa a eliminação de uma candidatura em rascunho ou marca como desistência
        /// uma candidatura que já tenha sido submetida.
        /// </summary>
        /// <param name="id">Identificador da candidatura.</param>
        /// <returns>Redirecionamento para a listagem de candidaturas.</returns>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id) {
            try {
                var candidatura = await _context.Candidaturas.FindAsync(id);
                if (candidatura == null) {
                    TempData["Erro"] = "Candidatura não localizada.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var userIdOficial = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (candidatura.AlunoFK != userIdOficial) {
                    TempData["Erro"] = "Violação de permissões detetada.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                // Hard-Delete vs Soft-Delete
                // Só apaga fisicamente se for um rascunho puro (nunca submetido nem avaliado)
                if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmConstrucao && candidatura.OrigemCorrecao == EnumOrigemCorrecao.None) {
                    _context.Candidaturas.Remove(candidatura);
                }
                else {
                    // Se já tiver sido submetida, preserva o histórico do processo para fins estatísticos institucionais,
                    // aplicando a remoção lógica alterando o estado do fluxo para Desistência (Soft Delete)
                    if (candidatura.EstadoCandidatura != EnumEstadoCandidatura.Finalizado && candidatura.EstadoCandidatura != EnumEstadoCandidatura.Desistencia) {

                        candidatura.EstadoCandidatura = EnumEstadoCandidatura.Desistencia;

                        // Limpa a origem da correção para o selo atualizar corretamente para "Desistência" nas Views do GRI/Coordenador
                        candidatura.OrigemCorrecao = EnumOrigemCorrecao.None;
                        candidatura.DataValidacao = DateTime.Now;

                        _context.Update(candidatura);
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex) {
                LogService.RegistarErro("DeleteConfirmed POST", ex);
                TempData["Erro"] = "Ocorreu um erro na Base de Dados ao tentar processar o cancelamento do processo.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: Candidaturas/UploadDocumento
        /// <summary>
        /// Adiciona um documento a uma candidatura editável do aluno.
        /// Valida a categoria, descrição, tamanho e extensão do ficheiro antes de guardar.
        /// </summary>
        /// <param name="candidaturaId">Identificador da candidatura.</param>
        /// <param name="categoriaDocumento">Categoria do documento.</param>
        /// <param name="descricaoDocumento">Descrição do documento, obrigatória quando a categoria é Outro.</param>
        /// <param name="ficheiro">Ficheiro enviado pelo aluno.</param>
        /// <returns>Redirecionamento para a página de edição ou detalhe da candidatura.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDocumento(Guid candidaturaId, string categoriaDocumento, string? descricaoDocumento, IFormFile ficheiro) {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            try {
                var candidatura = await _context.Candidaturas
                    .FirstOrDefaultAsync(c => c.Id == candidaturaId && c.AlunoFK == userId);

                // Garante que o aluno só envia documentos para candidaturas próprias.
                if (candidatura == null) {
                    TempData["Erro"] = "Operação de upload inválida ou documento intercetado.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var urlDocumentos = Url.Action(nameof(Edit), new { id = candidaturaId }) + "#documentos-candidatura";

            // Só permite alterar documentos enquanto a candidatura ainda pode ser editada pelo aluno.
            if (candidatura.EstadoCandidatura != EnumEstadoCandidatura.EmConstrucao && candidatura.EstadoCandidatura != EnumEstadoCandidatura.DevolvidaParaCorrecao )
            {
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                var categoriaExiste = await _context.CategoriasDocumentos
                    .AnyAsync(c => c.Nome == categoriaDocumento);

                if (!categoriaExiste) {
                    return Redirect(urlDocumentos);
                }

                if (categoriaDocumento == "Participant Report") {
                    TempData["Erro"] = "O Participant Report só pode ser submetido na fase pós-mobilidade.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                if (categoriaDocumento == "Outro" && string.IsNullOrWhiteSpace(descricaoDocumento)) {
                    return Redirect(urlDocumentos);
                }

                if (ficheiro == null || ficheiro.Length == 0) {
                    return Redirect(urlDocumentos);
                }

                const long tamanhoMaximo = 10 * 1024 * 1024; // 10 MB

                if (ficheiro.Length > tamanhoMaximo) {
                    return Redirect(urlDocumentos);
                }

                var extensoesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".docx" };
                var extensao = Path.GetExtension(ficheiro.FileName).ToLowerInvariant();

                if (!extensoesPermitidas.Contains(extensao)) {
                    return Redirect(urlDocumentos);
                }

                await GuardarDocumentoAsync(candidaturaId, userId!, categoriaDocumento, descricaoDocumento, ficheiro);
                await _context.SaveChangesAsync();

                return Redirect(urlDocumentos);
            }
            catch (Exception ex) {
                LogService.RegistarErro("UploadDocumento POST", ex);
                TempData["Erro"] = "Erro crítico de persistência de arquivos.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Candidaturas/DownloadDocumento
        /// <summary>
        /// Permite ao aluno descarregar um documento associado a uma das suas candidaturas.
        /// </summary>
        /// <param name="documentoId">Identificador do documento.</param>
        /// <returns>Ficheiro físico para download ou erro de acesso quando aplicável.</returns>
        public async Task<IActionResult> DownloadDocumento(Guid documentoId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            try {
                var documento = await _context.Documentos
                    .Include(d => d.Candidatura)
                    .FirstOrDefaultAsync(d =>
                        d.Id == documentoId &&
                        d.Candidatura.AlunoFK == userId);

                // Garante que o aluno só descarrega documentos das próprias candidaturas.
                if (documento == null) {
                    TempData["Erro"] = "Acesso Negado ou ficheiro inexistente.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var caminhoCompleto = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    documento.CaminhoFicheiroSistema
                );

                if (!System.IO.File.Exists(caminhoCompleto)) {
                    TempData["Erro"] = "O documento físico não se encontra no servidor.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var extensao = Path.GetExtension(documento.NomeOriginal).ToLowerInvariant();

                var contentType = extensao switch {
                    ".pdf" => "application/pdf",
                    ".jpg" => "image/jpeg",
                    ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    _ => "application/octet-stream"
                };

                return PhysicalFile(caminhoCompleto, contentType, documento.NomeOriginal);
            }
            catch (Exception ex) {
                LogService.RegistarErro("DownloadDocumento GET", ex);
                TempData["Erro"] = "Falha de processamento de download.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Candidaturas/RemoverDocumento
        /// <summary>
        /// Remove um documento de uma candidatura editável do aluno,
        /// apagando também o ficheiro físico do sistema quando existir.
        /// </summary>
        /// <param name="documentoId">Identificador do documento a remover.</param>
        /// <returns>Redirecionamento para a página de edição ou detalhe da candidatura.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoverDocumento(Guid documentoId) {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            try {
                var documento = await _context.Documentos
                    .Include(d => d.Candidatura)
                    .FirstOrDefaultAsync(d =>
                        d.Id == documentoId &&
                        d.Candidatura.AlunoFK == userId);

                // Garante que o aluno só remove documentos das próprias candidaturas.
                if (documento == null) {
                    TempData["Erro"] = "Arquivo não localizado ou violação de conta.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var urlDocumentos = Url.Action(nameof(Edit), new { id = documento.CandidaturaFK }) + "#documentos-candidatura";

            // Só permite remover enquanto a candidatura ainda pode ser editada pelo aluno.
            if (documento.Candidatura.EstadoCandidatura != EnumEstadoCandidatura.EmConstrucao && documento.Candidatura.EstadoCandidatura != EnumEstadoCandidatura.DevolvidaParaCorrecao)
            {
                    return RedirectToAction(nameof(Details), new { id = documento.CandidaturaFK });
                }

                var caminhoCompleto = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    documento.CaminhoFicheiroSistema
                );

                if (System.IO.File.Exists(caminhoCompleto)) {
                    System.IO.File.Delete(caminhoCompleto);
                }

                _context.Documentos.Remove(documento);
                await _context.SaveChangesAsync();

                return Redirect(urlDocumentos);
            }
            catch (Exception ex) {
                LogService.RegistarErro("RemoverDocumento POST", ex);
                TempData["Erro"] = "Erro ao tentar expurgar o documento.";
                return RedirectToAction(nameof(Index));
            }
        }


        // =========================================================================
        // MÉTODOS AUXILIARES PRIVADOS (Prevenção de Duplicação de Código)
        // =========================================================================

        /// <summary>
        /// Repovoa as listas auxiliares usadas nos formulários de criação e edição,
        /// nomeadamente universidades e anos letivos.
        /// </summary>
        private void RepovoarListasApoio() {
            ViewBag.Universidades = new SelectList(ObterListaUniversidadesFormatada(), "ErasmusCode", "Nome", null, "GrupoOrganizado");
            GerarAnosLetivosDinamicos();
            CarregarCodigosOutraUniversidade();
        }

        /// <summary>
        /// Obtém a lista de universidades formatada e agrupada por tipo de acordo e país,
        /// para utilização nas dropdowns dos formulários.
        /// </summary>
        /// <returns>Lista de universidades formatada para SelectList.</returns>
        private System.Collections.IEnumerable ObterListaUniversidadesFormatada() {
            try {
                return _context.Universidades
                    .OrderBy(u => u.TipoAcordo).ThenBy(u => u.Pais).ThenBy(u => u.Nome)
                    .Select(u => new {
                        ErasmusCode = u.ErasmusCode,
                        Nome = u.Nome,
                        GrupoOrganizado = u.TipoAcordo == "Acordos Bilaterais Erasmus+" ? $"Erasmus+ ({u.Pais})" : $"Internacional ({u.Pais})"
                    }).ToList();
            }
            catch (Exception ex) {
                LogService.RegistarErro("ObterListaUniversidadesFormatada", ex);
                return new List<object>();
            }
        }

        /// <summary>
        /// Cria iterativamente uma gama coerente (de +3 anos fixos) do presente calendário para mitigar envios 
        /// inconsistentes (como "1994/1995") na hora da submissão da candidatura.
        /// </summary>
        private void GerarAnosLetivosDinamicos() {
            ViewBag.AnosLetivos = new SelectList(ObterAnosLetivosPermitidos());
        }

        /// <summary>
        /// Carrega as categorias de documentos disponíveis para as dropdowns dos formulários.
        /// </summary>
        private async Task CarregarCategoriasDocumentosAsync() {
            try {
                // 1. Criamos uma lista com os nomes exatos que queremos ESCONDER do aluno
                var documentosExcluidos = new List<string> {
                    "Participant Report",
                    "Transcrição de Notas",
                    "Ficha de Seleção",
                    "Reconhecimento Académico"
                };

                // 2. Filtramos a base de dados usando o !Contains (não contém na lista de excluídos)
                ViewBag.CategoriasDocumentos = await _context.CategoriasDocumentos
                    .Where(c => !documentosExcluidos.Contains(c.Nome))
                    .OrderBy(c => c.Nome)
                    .Select(c => new SelectListItem {
                        Value = c.Nome,
                        Text = c.Nome
                    })
                    .ToListAsync();
            }
            catch (Exception ex) {
                LogService.RegistarErro("CarregarCategoriasDocumentosAsync", ex);
                ViewBag.CategoriasDocumentos = new List<SelectListItem>();
            }
        }

        /// <summary>
        /// Guarda fisicamente um ficheiro enviado pelo aluno e cria o respetivo registo de documento na base de dados.
        /// </summary>
        /// <param name="candidaturaId">Identificador da candidatura associada ao documento.</param>
        /// <param name="userId">Identificador do utilizador que enviou o documento.</param>
        /// <param name="categoriaDocumento">Categoria do documento.</param>
        /// <param name="descricaoDocumento">Descrição do documento, usada quando a categoria é Outro.</param>
        /// <param name="ficheiro">Ficheiro enviado.</param>
        private async Task GuardarDocumentoAsync(Guid candidaturaId, string userId, string categoriaDocumento, string? descricaoDocumento, IFormFile ficheiro){
            var documentoId = Guid.NewGuid();
            var extensao = Path.GetExtension(ficheiro.FileName).ToLowerInvariant();
            var nomeFisico = $"{documentoId}{extensao}";

            var pastaDestino = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "candidaturas",
                candidaturaId.ToString()
            );

            Directory.CreateDirectory(pastaDestino);

            var caminhoCompleto = Path.Combine(pastaDestino, nomeFisico);

            using (var stream = new FileStream(caminhoCompleto, FileMode.Create)) {
                await ficheiro.CopyToAsync(stream);
            }

            var descricaoFinal = categoriaDocumento == "Outro"
                ? descricaoDocumento?.Trim() ?? string.Empty
                : string.Empty;

            var documento = new Documento {
                Id = documentoId,
                NomeOriginal = Path.GetFileName(ficheiro.FileName),
                CaminhoFicheiroSistema = $"uploads/candidaturas/{candidaturaId}/{nomeFisico}",
                CategoriaFK = categoriaDocumento,
                CandidaturaFK = candidaturaId,
                CreatedByFK = userId,
                UpdatedByFK = userId,
                DataCriacao = DateTime.Now,
                DataAtualizacao = DateTime.Now,
                DescricaoDocumento = descricaoFinal
            };

            try {
                _context.Documentos.Add(documento);
            }
            catch (Exception ex) {
                LogService.RegistarErro("GuardarDocumentoAsync", ex);
                throw; // Repropaga para o método Post transacional gerir o erro de forma global
            }
        }

        /// <summary>
        /// Valida os documentos enviados no formulário de criação,
        /// verificando categoria, descrição obrigatória, tamanho máximo e extensão permitida.
        /// </summary>
        /// <param name="categoriasDocumento">Categorias selecionadas para os documentos.</param>
        /// <param name="descricoesDocumento">Descrições associadas aos documentos.</param>
        /// <param name="ficheirosDocumento">Ficheiros enviados pelo aluno.</param>
        private async Task ValidarDocumentosAsync(List<string> categoriasDocumento, List<string?> descricoesDocumento, List<IFormFile> ficheirosDocumento) {
            if (categoriasDocumento == null) categoriasDocumento = [];
            if (descricoesDocumento == null) descricoesDocumento = [];
            if (ficheirosDocumento == null) ficheirosDocumento = [];

            const long tamanhoMaximo = 10 * 1024 * 1024;
            var extensoesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".docx" };

            try {
                for (int i = 0; i < ficheirosDocumento.Count; i++) {
                    var ficheiro = ficheirosDocumento[i];

                    if (ficheiro == null || ficheiro.Length == 0)
                    {
                        continue;
                    }

                    var categoria = i < categoriasDocumento.Count ? categoriasDocumento[i] : string.Empty;
                    var descricao = i < descricoesDocumento.Count ? descricoesDocumento[i] : string.Empty;

                    var categoriaExiste = await _context.CategoriasDocumentos
                        .AnyAsync(c => c.Nome == categoria);

                    if (!categoriaExiste) {
                        ModelState.AddModelError(string.Empty, "Um ou mais documentos têm uma categoria inválida.");
                        continue;
                    }
                    if (categoria == "Participant Report")
                    {
                        ModelState.AddModelError(string.Empty, "O Participant Report só pode ser submetido na fase pós-mobilidade.");
                        continue;
                    }

                    if (categoria == "Outro" && string.IsNullOrWhiteSpace(descricao)) {
                        ModelState.AddModelError(string.Empty, "Quando o tipo de documento é Outro, deve ser indicada uma descrição.");
                    }

                    if (ficheiro.Length > tamanhoMaximo) {
                        ModelState.AddModelError(string.Empty, "Um ou mais documentos excedem o tamanho máximo permitido de 10 MB.");
                    }

                    var extensao = Path.GetExtension(ficheiro.FileName).ToLowerInvariant();

                    if (!extensoesPermitidas.Contains(extensao)) {
                        ModelState.AddModelError(string.Empty, "Um ou mais documentos têm um formato inválido.");
                    }
                }
            }
            catch (Exception ex) {
                LogService.RegistarErro("ValidarDocumentosAsync", ex);
                ModelState.AddModelError(string.Empty, "Ocorreu uma falha técnica ao validar as categorias de documentos na Base de Dados.");
            }
        }

        // POST: Candidaturas/SubmeterDadosFinais
        /// <summary>
        /// Recebe e valida o documento obrigatório de regresso do aluno (Participant Report),
        /// avançando o processo para a validação final do GRI.
        /// </summary>
        /// <param name="id">GUID do Processo.</param>
        /// <param name="ficheiroParticipantReport">A documentação da Comissão Europeia formalmente preenchida (PDF obrigatório).</param>
        /// <returns>Alertas e rotas reprocessadas ao nível de Base de Dados.</returns> [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmeterDadosFinais(Guid id, IFormFile ficheiroParticipantReport) {
            var userIdOficial = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdOficial)) {
                TempData["Erro"] = "Sessão inválida.";
                return RedirectToAction("AcessoRestrito", "Home");
            }

            try {
                var candidatura = await _context.Candidaturas.Include(c => c.Aluno).FirstOrDefaultAsync(c => c.Id == id);

                // Segurança: Verifica a existência e integridade do dono do registo
                var ehPrimeiraSubmissaoFinal = candidatura?.EstadoCandidatura == EnumEstadoCandidatura.Aceite;

                var correcaoFinal = candidatura?.EstadoCandidatura == EnumEstadoCandidatura.DevolvidaParaCorrecao && candidatura.OrigemCorrecao == EnumOrigemCorrecao.GRI && await RequerCorrecaoDocumentosFinaisAsync(candidatura.Id);

                if (candidatura == null ||
                    candidatura.AlunoFK != userIdOficial ||
                    (!ehPrimeiraSubmissaoFinal && !correcaoFinal)) {
                    TempData["Erro"] = "Ação ilegal ou processo indisponível para fecho.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                // Validação de segurança no Servidor: Garante que o ficheiro não veio vazio
                if (ficheiroParticipantReport == null || ficheiroParticipantReport.Length == 0) {
                    TempData["Erro"] = "Deves anexar obrigatoriamente o Participant Report.";
                    return RedirectToAction(nameof(Details), new { id = candidatura.Id });
                }

                if (Path.GetExtension(ficheiroParticipantReport.FileName).ToLowerInvariant() != ".pdf") {
                    TempData["Erro"] = "O Participant Report deve ser submetido obrigatoriamente em formato PDF.";
                    return RedirectToAction(nameof(Details), new { id = candidatura.Id });
                }
                // Processar e guardar o Participant Report
                await GuardarDocumentoAsync(
                    candidaturaId: candidatura.Id,
                    userId: userIdOficial,
                    categoriaDocumento: "Participant Report",
                    descricaoDocumento: "",
                    ficheiro: ficheiroParticipantReport
                );
                candidatura.PermiteCorrecao = false;
                candidatura.OrigemCorrecao = EnumOrigemCorrecao.None;
                candidatura.MotivoRejeicao = string.Empty;

                // Evolução da máquina de estados
                candidatura.EstadoCandidatura = EnumEstadoCandidatura.EmAnaliseFinalGRI;
                candidatura.DataValidacao = DateTime.Now;

                await _context.SaveChangesAsync();

                // INÍCIO DO BLOCO DE EMAIL DE REGRESSO
                try {
                    string linkDireto = Url.Action("Details", "GRI", new { id = candidatura.Id }, protocol: Request.Scheme) ?? string.Empty;

                    // Proteção contra nulos (warnings) no nome do aluno
                    string nomeAluno = candidatura.Aluno?.NomeCompleto ?? "Aluno";

                    string assunto = $"[Portal Erasmus] Pós-Mobilidade: Participant Report Recebido - {nomeAluno}";
                    string mensagemHtml = $@"
                        <div style='font-family: sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #eee; padding: 20px; border-radius: 8px;'>
                            <h2 style='color: #0d6efd;'>Fase Pós-Mobilidade Iniciada</h2>
                            <p>Atenção GRI,</p>
                            <p>O aluno <strong>{nomeAluno}</strong> regressou da sua mobilidade e submeteu o seu <strong>Participant Report</strong>.</p>
                            <p>O processo encontra-se a aguardar a validação final da vossa parte para proceder ao encerramento.</p>
                            <div style='margin: 30px 0; text-align: center;'>
                                <a href='{linkDireto}' style='background-color: #0d6efd; color: white; padding: 12px 24px; text-decoration: none; font-weight: bold; border-radius: 5px; display: inline-block;'>
                                    Validar Documentação Final
                                </a>
                            </div>
                            <hr style='border: 0; border-top: 1px solid #eee;' />
                            <p style='font-size: 0.8rem; color: #777;'>Este é um email automático gerado pelo Portal Erasmus@IPT.</p>
                        </div>";

                    // BUSCA DINÂMICA DOS UTILIZADORES COM A ROLE GRI
                    var roleGriId = await _context.Roles
                        .Where(r => r.Name == "GRI")
                        .Select(r => r.Id)
                        .FirstOrDefaultAsync();

                    var emailsGRI = await _context.UserRoles
                        .Where(ur => ur.RoleId == roleGriId)
                        .Join(_context.Users, ur => ur.UserId, u => u.Id, (ur, u) => u.Email)
                        .Where(e => !string.IsNullOrEmpty(e))
                        .ToListAsync();

                    // DISPARO DOS EMAILS (COM FALLBACK)
                    if (emailsGRI.Any()) {
                        foreach (var email in emailsGRI) {
                            // APLICADO O TRUQUE DE PROTEÇÃO DO COMPILADOR
                            string targetEmail = email ?? string.Empty;

                            // Só para dupla segurança e evitar que o EnviarEmailAsync reclame do vazio
                            if (!string.IsNullOrEmpty(targetEmail)) {
                                _ = Task.Run(async () => await _servicoEmail.EnviarEmailAsync(targetEmail, assunto, mensagemHtml));
                            }
                        }
                    }
                    else {
                        // Fallback: Se não houver utilizadores GRI na base de dados, envia para a conta de testes
                        _ = Task.Run(async () => await _servicoEmail.EnviarEmailAsync(_emailGRI, assunto, mensagemHtml));
                    }
                }
                catch (Exception ex) {
                    LogService.RegistarErro("Lógica Email Regresso Aluno", ex);
                }
                // FIM DO BLOCO DE EMAIL

                TempData["MensagemSucesso"] = "Participant Report guardado com sucesso! O teu processo foi enviado para o fecho regulamentar do GRI.";
            }
            catch (Exception ex) {
                LogService.RegistarErro("SubmeterDadosFinais POST", ex);
                TempData["Erro"] = "Ocorreu uma falha na Base de Dados ao tentar arquivar a documentação final.";
            }

            return RedirectToAction(nameof(Details), new { id = id });
        }

        /// <summary>
        /// Proíbe duplicatas, escolhas nulas ou inexistentes nas opções de universidades de destino.
        /// Aborda com fiabilidade a classificação "Outro" requerida por Instituições Não Catalogadas.
        /// </summary>
        private void ValidarUniversidadesEscolhidas(
            string? universidade1,
            string? universidade2,
            string? universidade3,
            string? universidade1Other,
            string? universidade2Other,
            string? universidade3Other) {
            if (string.IsNullOrWhiteSpace(universidade1)) {
                ModelState.AddModelError(string.Empty, "Tens de escolher obrigatoriamente uma universidade como 1ª Opção.");
                return;
            }

            var unisEscolhidas = new List<string> {
                universidade1,
                universidade2 ?? string.Empty,
                universidade3 ?? string.Empty
            }.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();

            var codigosExistentes = _context.Universidades
                .Select(u => u.ErasmusCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (unisEscolhidas.Any(u => !codigosExistentes.Contains(u)))
            {
                ModelState.AddModelError(string.Empty, "Uma ou mais universidades selecionadas são inválidas.");
            }

            var codigosOutra = ObterCodigosOutraUniversidade();
            var escolhasNormalizadas = unisEscolhidas
                .Select(u => codigosOutra.Contains(u) ? "__OUTRA__" : u)
                .ToList();

            if (escolhasNormalizadas.Distinct().Count() != escolhasNormalizadas.Count) {
                ModelState.AddModelError(string.Empty, "Não podes escolher a mesma universidade em opções diferentes.");
            }

            ValidarNomeOutraUniversidade(universidade1, universidade1Other, "1ª opção");
            ValidarNomeOutraUniversidade(universidade2, universidade2Other, "2ª opção");
            ValidarNomeOutraUniversidade(universidade3, universidade3Other, "3ª opção");
        }

        /// <summary>
        /// Exige rigor caso seja acionado a flag "Outro" no dropdown.
        /// </summary>
        private void ValidarNomeOutraUniversidade(string? codigoUniversidade, string? nomeOutra, string labelOpcao) {
            if (IsOutraUniversidade(codigoUniversidade) && string.IsNullOrWhiteSpace(nomeOutra)) {
                ModelState.AddModelError(string.Empty, $"Quando escolhes 'Outra' na {labelOpcao}, tens de escrever o nome da universidade.");
            }
        }

        /// <summary>
        /// Obtém os códigos identificadores de outra universidade.
        /// </summary>
        private HashSet<string> ObterCodigosOutraUniversidade() {
            try {
                return _context.Universidades
                    .Where(u => (u.ErasmusCode != null && u.ErasmusCode.ToLower() == "other")
                                || (u.Nome != null && (u.Nome.ToLower().Contains("outra") || u.Nome.ToLower().Contains("other"))))
                    .Select(u => u.ErasmusCode)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) {
                LogService.RegistarErro("ObterCodigosOutraUniversidade", ex);
                return [];
            }
        }

        /// <summary>
        /// Confirma se o código facultado pertence ao registo universal/técnico de nome "Outro".
        /// </summary>
        private bool IsOutraUniversidade(string? codigoUniversidade) {
            return !string.IsNullOrWhiteSpace(codigoUniversidade)
                && ObterCodigosOutraUniversidade().Contains(codigoUniversidade);
        }

        /// <summary>
        /// Injete na ViewBag o hash map dos códigos técnicos que assinalam o estado "Outro".
        /// </summary>
        private void CarregarCodigosOutraUniversidade() {
            ViewBag.OutraUniversidadeCodes = ObterCodigosOutraUniversidade().ToList();
        }

        /// <summary>
        /// Rotina auxiliar de mapeamento N:M (Candidaturas -> Universidades).
        /// Abstrai a lógica de ordenação e preferência declarada.
        /// </summary>
        private List<CandidaturaUniversidade> ConstruirCandidaturasUniversidades(
            string? universidade1,
            string? universidade2,
            string? universidade3,
            string? universidade1Other,
            string? universidade2Other,
            string? universidade3Other) {
            var escolhas = new (string? Code, string? Other)[]
            {
                (universidade1, universidade1Other),
                (universidade2 ?? string.Empty, universidade2Other),
                (universidade3 ?? string.Empty, universidade3Other)
            };

            var resultado = new List<CandidaturaUniversidade>();
            int ordem = 1;

            foreach (var (code, other) in escolhas) {
                if (string.IsNullOrWhiteSpace(code)) {
                    continue;
                }

                resultado.Add(new CandidaturaUniversidade {
                    UniversidadeFK = code,
                    Preferencia = ordem,
                    OutraUniversidade = IsOutraUniversidade(code) ? other?.Trim() : null,
                    EstadoCandidatura = EnumEstadoCandidaturaUniversidade.EmProcessamento,
                    Candidatura = null!,
                    Universidade = null!
                });
                ordem++;
            }

            return resultado;
        }

        /// <summary>
        /// Previne injeção temporal ao disponibilizar estritamente os anos vindouros face ao momento do Servidor.
        /// </summary>
        private List<string> ObterAnosLetivosPermitidos() {
            int anoAtual = DateTime.Now.Year;
            var listaAnos = new List<string>();

            for (int i = 0; i < 3; i++) {
                listaAnos.Add($"{anoAtual + i}/{anoAtual + i + 1}");
            }

            return listaAnos;
        }

        /// <summary>
        /// Verificação dupla no backend se o ano enviado pertence ao lote dinâmico atual.
        /// </summary>
        private bool AnoLetivoValido(string? anoLetivo) {
            if (string.IsNullOrWhiteSpace(anoLetivo)) {
                return false;
            }

            return ObterAnosLetivosPermitidos().Contains(anoLetivo.Trim());
        }

        /// <summary>
        /// Confirmação de integridade para a Enumeration dos semestres.
        /// </summary>
        private static bool SemestreValido(EnumSemestre? semestre) {
            return semestre.HasValue && Enum.IsDefined(typeof(EnumSemestre), semestre.Value);
        }

        /// <summary>
        /// Avalia as exigências impostas para fecho da Pós-Mobilidade (Participant Report).
        /// </summary>
        private async Task<bool> RequerCorrecaoDocumentosFinaisAsync(Guid candidaturaId) {
            return await _context.Candidaturas
                .AnyAsync(c =>
                    c.Id == candidaturaId &&
                    c.EstadoCandidatura == EnumEstadoCandidatura.DevolvidaParaCorrecao &&
                    c.OrigemCorrecao == EnumOrigemCorrecao.GRI &&
                    c.Documentos.Any(d => d.CategoriaFK == "Participant Report"));
        }
    }

    /// <summary>
    /// View Model (DTO) utilizado para transportar dados entre o formulário de candidatura (View) e o Controller.
    /// Garante a validação (Client-Side e Server-Side) das regras de negócio antes da persistência na Base de Dados.
    /// Separa a lógica de apresentação da lógica estrutural (Entities) para evitar ataques Over-Posting.
    /// </summary>
    public class CandidaturaFormViewModel
    {
        /// <summary>
        /// Identificador único da candidatura. Será nulo na fase de criação da mesma (HTTP GET Create).
        /// </summary>
        public Guid? Id { get; set; }

        /// <summary>
        /// O ano letivo escolhido para a mobilidade (Ex: 2026/2027).
        /// </summary>
        [Required(ErrorMessage = "O ano letivo é obrigatório.")]
        [Display(Name = "Ano Letivo de Candidatura")]
        public string? AnoLetivo { get; set; }

        /// <summary>
        /// Semestre (1º, 2º ou Anual) no qual decorrerá a viagem pedagógica.
        /// </summary>
        [Required(ErrorMessage = "O semestre da mobilidade é obrigatório.")]
        [Display(Name = "Semestre da Mobilidade")]
        public EnumSemestre? Semestre { get; set; }

        /// <summary>
        /// Propriedade que espelha os apoios financeiros sociais que o aluno aufere.
        /// </summary>
        [Display(Name = "Bolseiro")]
        public bool Bolseiro { get; set; }

        /// <summary>
        /// Registo financeiro associado ao processo do aluno (Submetido a forte validação Regex para SEPA).
        /// </summary>
        [Required(ErrorMessage = "O IBAN é obrigatório.")]
        [Display(Name = "IBAN")]
        [StringLength(50, ErrorMessage = "O IBAN não pode exceder os 50 caracteres.")]
        [RegularExpression(@"^[A-Z]{2}\d{2}[A-Z0-9\s]{11,30}$", ErrorMessage = "Formato de IBAN inválido.")]
        public string? IBAN { get; set; }

        /// <summary>
        /// O identificador relacional para a chave primária do curso que credencia a saída do estudante.
        /// </summary>
        [Required(ErrorMessage = "O curso associado à candidatura é obrigatório.")]
        [Range(1, int.MaxValue, ErrorMessage = "O curso associado à candidatura é obrigatório.")]
        [Display(Name = "Curso da Candidatura")]
        public int? CursoFK { get; set; }

        /// <summary>
        /// A declaração das competências comunicativas submetidas nesta sessão do formulário.
        /// </summary>
        [Display(Name = "Competências Linguísticas")]
        public List<CandidaturaLingua> Linguas { get; set; } = new List<CandidaturaLingua>();

        /// <summary>
        /// Os metadados de anexos (usados para a listagem visual durante a fase de Edit/Details).
        /// </summary>
        public List<Documento> Documentos { get; set; } = new List<Documento>();

        /// <summary>
        /// A declaração estruturada e hierarquizada das escolhas geográficas (Universidades e Países).
        /// </summary>
        public List<CandidaturaUniversidade> CandidaturasUniversidades { get; set; } = new List<CandidaturaUniversidade>();

        /// <summary>
        /// Reflexo da fase atual da máquina de estados do Backoffice. Controla que botões aparecem em tempo de interface.
        /// </summary>
        public EnumEstadoCandidatura EstadoCandidatura { get; set; }

        /// <summary>
        /// Identifica a proveniência da última devolução do processo para mostrar selos na interface gráfica de correção.
        /// </summary>
        public EnumOrigemCorrecao OrigemCorrecao { get; set; } = EnumOrigemCorrecao.None;

        /// <summary>
        /// Justificação textual explícita da Rejeição ou Devolução pela máquina administrativa.
        /// </summary>
        public string MotivoRejeicao { get; set; } = string.Empty;
    }
}