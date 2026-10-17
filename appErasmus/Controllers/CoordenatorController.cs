using appErasmus.Data;
using appErasmus.Models;
using appErasmus.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using System.Linq; // Garante que tens este using para o OrderBy e Where funcionar a 100%
using System.Security.Claims;

namespace appErasmus.Controllers {

    /// <summary>
    /// Controller responsável pela área do Coordenador Erasmus.
    /// Permite consultar candidaturas dos cursos atribuídos ao coordenador,
    /// visualizar detalhes, descarregar documentos e decidir o estado inicial da candidatura.
    /// </summary>
    [Authorize(Roles = "CoordenadorErasmus")]
    public class CoordenatorController : Controller {

        private readonly ApplicationDbContext _context;
        private readonly IServicoEmail _servicoEmail;
        private readonly string _emailGRI;

        /// <summary>
        /// Inicializa o controller com o contexto da base de dados.
        /// </summary>
        /// <param name="context">Contexto da aplicação usado para aceder às candidaturas e documentos.</param>
        /// <param name="servicoEmail">Serviço de envio de emails.</param>
        public CoordenatorController(ApplicationDbContext context, IServicoEmail servicoEmail, IConfiguration configuration) {
            _context = context;
            _servicoEmail = servicoEmail;
            _emailGRI = configuration["ContactosInstitucionais:EmailGRI"] ?? throw new InvalidOperationException("O email do GRI não está configurado em appsettings.json.");
        }

        // GET: Coordenator
        /// <summary>
        /// Lista as candidaturas submetidas associadas aos cursos coordenados pelo utilizador autenticado.
        /// Exclui candidaturas ainda em rascunho, por serem privadas do aluno.
        /// </summary>
        /// <returns>View com a lista de candidaturas acessíveis ao coordenador.</returns>
        public async Task<IActionResult> Index() {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            try {
                var todasCandidaturas = await _context.Candidaturas
                .Include(c => c.Aluno)
                .Include(c => c.Curso)
                    .ThenInclude(curso => curso.Escola)
                .Include(c => c.Curso)
                    .ThenInclude(curso => curso.GrauCurso)
                .Include(c => c.Linguas)
                .Where(c => c.EstadoCandidatura != EnumEstadoCandidatura.EmConstrucao || c.OrigemCorrecao == EnumOrigemCorrecao.Coordenador)
                .Where(c => c.Curso.CoordenadorFK == userId)
                .OrderByDescending(c => c.DataSubmissao)
                .ToListAsync();

                return View(todasCandidaturas);
            }
            catch (Exception ex) {
                LogService.RegistarErro("Coordenator Index GET", ex);
                return View(new List<Candidatura>());
            }
        }

        // GET: Coordenator/Details/5
        /// <summary>
        /// Mostra os detalhes completos de uma candidatura acessível ao coordenador,
        /// incluindo dados do aluno, curso, universidades, línguas e documentos anexados.
        /// </summary>
        /// <param name="id">Identificador da candidatura.</param>
        /// <returns>View de detalhe da candidatura ou erro de acesso quando aplicável.</returns>
        public async Task<IActionResult> Details(Guid? id) {
            if (id == null) {
                TempData["Erro"] = "Processo não especificado.";
                return RedirectToAction("AcessoRestrito", "Home");
            }

            try {
                var candidatura = await _context.Candidaturas
                .Include(c => c.Aluno)
                .Include(c => c.Curso)
                    .ThenInclude(curso => curso.Escola)
                .Include(c => c.Curso)
                    .ThenInclude(curso => curso.GrauCurso)
                .Include(c => c.Linguas)
                .Include(c => c.Documentos.Where(d => !d.CategoriaFK.Contains("IBAN")))
                    .ThenInclude(d => d.Categoria)
                .Include(c => c.Documentos.Where(d => !d.CategoriaFK.Contains("IBAN")))
                    .ThenInclude(d => d.CreatedBy)
                .Include(c => c.Documentos.Where(d => !d.CategoriaFK.Contains("IBAN")))
                    .ThenInclude(d => d.UpdatedBy)
                .Include(c => c.CandidaturasUniversidades)
                    .ThenInclude(cu => cu.Universidade)
                .FirstOrDefaultAsync(m => m.Id == id);

                // SEGURANÇA EXTRA: Se a candidatura não existir OU ainda for um rascunho em construção,
                // o sistema barra o acesso visual e dá NotFound, protegendo a privacidade do aluno.
                if (candidatura == null || (candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmConstrucao && candidatura.OrigemCorrecao != EnumOrigemCorrecao.Coordenador)) {
                    TempData["Erro"] = "O processo procurado não existe ou não está disponível.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (!await CoordenadorPodeAcederCandidaturaAsync(candidatura)) {
                    TempData["Erro"] = "Acesso negado. Esta candidatura não pertence aos teus cursos coordenados.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var categoriasJaExistentes = await _context.Documentos
                    .Where(d => d.CandidaturaFK == candidatura.Id)
                    .Select(d => d.CategoriaFK)
                    .ToListAsync();

                ViewBag.CategoriasDocumento = await _context.CategoriasDocumentos
                    .Where(c => !c.Nome.Contains("IBAN"))
                    .Where(c => !c.Nome.Contains("Participant Report"))
                    .Where(c =>
                        !categoriasJaExistentes.Contains(c.Nome) ||
                        c.Nome == "Outro")
                    .OrderBy(c => c.Nome)
                    .ToListAsync();

                return View(candidatura);
            }
            catch (Exception ex) {
                LogService.RegistarErro("Coordenator Details GET", ex);
                TempData["Erro"] = "Erro interno ao processar a consulta.";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Verifica se o coordenador autenticado pode aceder à candidatura indicada.
        /// O acesso só é permitido quando a candidatura pertence a um curso atribuído ao coordenador.
        /// </summary>
        /// <param name="candidatura">Candidatura a validar.</param>
        /// <returns>True se o coordenador puder aceder; caso contrário, false.</returns>
        private async Task<bool> CoordenadorPodeAcederCandidaturaAsync(Candidatura candidatura) {
            if (!User.IsInRole("CoordenadorErasmus")) {
                return false;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId)) {
                return false;
            }

            try {
                return await _context.Candidaturas
                .AnyAsync(c =>
                    c.Id == candidatura.Id &&
                    c.Curso.CoordenadorFK == userId);
            }
            catch (Exception ex) {
                LogService.RegistarErro("CoordenadorPodeAcederCandidaturaAsync", ex);
                return false;
            }
        }

        // POST: Coordenator/AlterarEstado
        /// <summary>
        /// Altera o estado de uma candidatura na fase de decisão do coordenador.
        /// Permite aprovar para análise do GRI, rejeitar definitivamente ou devolver ao aluno para correção.
        /// </summary>
        /// <param name="id">Identificador da candidatura.</param>
        /// <param name="acao">Ação executada pelo coordenador.</param>
        /// <param name="observacoesDecisao">Motivo da rejeição ou devolução.</param>
        /// <param name="permiteCorrecao">Indica se o aluno pode corrigir e voltar a submeter.</param>
        /// <returns>Redirecionamento para os detalhes da candidatura.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AlterarEstado(Guid id, string acao, string? observacoesDecisao, bool permiteCorrecao = false) {
            try {
                var candidatura = await _context.Candidaturas.Include(c => c.Aluno).FirstOrDefaultAsync(c => c.Id == id);

                if (candidatura == null || candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmConstrucao) {
                    TempData["Erro"] = "Candidatura inválida ou sob preenchimento.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (!await CoordenadorPodeAcederCandidaturaAsync(candidatura)) {
                    TempData["Erro"] = "Operação negada por restrições de perfil.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                // O coordenador só deve decidir candidaturas que estão submetidas para análise.
                if (candidatura.EstadoCandidatura != EnumEstadoCandidatura.Submetida) {
                    TempData["Erro"] = "O processo já se encontra fora da alçada de avaliação da coordenação.";
                    return RedirectToAction(nameof(Details), new { id = candidatura.Id });
                }

                switch (acao) {
                    case "AprovarCoordenador":
                        candidatura.EstadoCandidatura = EnumEstadoCandidatura.EmAnaliseGRI;
                        candidatura.DataValidacao = DateTime.Now;
                        candidatura.PermiteCorrecao = false;
                        candidatura.OrigemCorrecao = EnumOrigemCorrecao.None;
                        candidatura.MotivoRejeicao = String.Empty;
                        break;

                    case "RejeitarCoordenador":
                        // Rejeições e devoluções exigem uma justificação visível para o aluno.
                        if (string.IsNullOrWhiteSpace(observacoesDecisao)) {
                            TempData["Erro"] = "Tens de indicar obrigatoriamente um motivo para a rejeição/devolução.";
                            return RedirectToAction(nameof(Details), new { id = candidatura.Id });
                        }

                        candidatura.MotivoRejeicao = observacoesDecisao.Trim();
                        candidatura.PermiteCorrecao = permiteCorrecao;
                        candidatura.DataValidacao = DateTime.Now;

                        if (permiteCorrecao) {
                            candidatura.EstadoCandidatura = EnumEstadoCandidatura.DevolvidaParaCorrecao;
                            candidatura.OrigemCorrecao = EnumOrigemCorrecao.Coordenador;
                        }
                        else {
                            candidatura.EstadoCandidatura = EnumEstadoCandidatura.RejeitadaCoordenador;
                            candidatura.OrigemCorrecao = EnumOrigemCorrecao.None;
                        }
                        break;

                    default:
                        return RedirectToAction(nameof(Details), new { id = candidatura.Id });
                }

                await _context.SaveChangesAsync();

                // INÍCIO DO BLOCO DE EMAIL DO COORDENADOR
                try {
                    // Mudámos para uma lista para suportar múltiplos destinatários (Role GRI)
                    var emailsDestino = new List<string>();

                    string linkDireto = "";
                    string assunto = "";
                    string tituloCorpo = "";
                    string textoCorpo = "";
                    string textoBotao = "Ver Candidatura";

                    if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmAnaliseGRI) {
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

                        if (emailsGRI.Any()) {
                            emailsDestino.AddRange(emailsGRI!);
                        }
                        else {
                            emailsDestino.Add(_emailGRI); // Fallback caso a Role esteja vazia
                        }

                        linkDireto = Url.Action("Details", "GRI", new { id = candidatura.Id }, protocol: Request.Scheme) ?? string.Empty;

                        assunto = $"[Portal Erasmus] Validação Académica Concluída - {candidatura.Aluno?.NomeCompleto}";
                        tituloCorpo = "Nova Candidatura para Análise GRI";
                        textoCorpo = $"A candidatura de mobilidade do aluno <strong>{candidatura.Aluno?.NomeCompleto}</strong> foi aprovada pelo Coordenador do Curso e aguarda agora a sua validação regulamentar.";
                        textoBotao = "Analisar Processo";
                    }
                    else if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.DevolvidaParaCorrecao) {
                        // Vai para o Aluno (Adiciona apenas 1 e-mail à lista)
                        string emailAluno = candidatura.Aluno?.Email ?? string.Empty;
                        if (!string.IsNullOrEmpty(emailAluno)) {
                            emailsDestino.Add(emailAluno);
                        }

                        linkDireto = Url.Action("Edit", "Candidaturas", new { id = candidatura.Id }, protocol: Request.Scheme) ?? string.Empty;

                        // Mantemos o padrão semântico suave para evitar os filtros de spam do Outlook
                        assunto = $"[Portal Erasmus] Revisão de Dados Necessária - {candidatura.Aluno?.NomeCompleto}";
                        tituloCorpo = "Análise Técnica ao Processo";
                        textoCorpo = $"A tua candidatura foi analisada pelo Coordenador Erasmus, mas existem alguns detalhes que precisam de ser revistos ou clarificados para o processo avançar:<br><br><em>\"{candidatura.MotivoRejeicao}\"</em>";
                        textoBotao = "Rever e Atualizar Processo";
                    }
                    else if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.RejeitadaCoordenador) {
                        // Vai para o Aluno (Adiciona apenas 1 e-mail à lista)
                        string emailAluno = candidatura.Aluno?.Email ?? string.Empty;
                        if (!string.IsNullOrEmpty(emailAluno)) {
                            emailsDestino.Add(emailAluno);
                        }
                        
                        linkDireto = Url.Action("Details", "Candidaturas", new { id = candidatura.Id }, protocol: Request.Scheme) ?? string.Empty;
                        assunto = $"[Portal Erasmus] Atualização do Estado da Candidatura - {candidatura.Aluno?.NomeCompleto}";
                        tituloCorpo = "Candidatura Rejeitada";
                        textoCorpo = $"Informamos que a tua candidatura de mobilidade foi rejeitada pelo Coordenador Erasmus. Motivo indicado:<br><br><em>\"{candidatura.MotivoRejeicao}\"</em>";
                    }

                    // DISPARO DOS EMAILS PARA TODA A LISTA MAREADA
                    if (emailsDestino.Any() && !string.IsNullOrEmpty(assunto)) {
                        string mensagemHtml = $@"<div style='font-family: sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #eee; padding: 20px; border-radius: 8px;'>                <h2 style='color: #0d6efd;'>{tituloCorpo}</h2>                <p>{textoCorpo}</p>                <div style='margin: 30px 0; text-align: center;'>                    <a href='{linkDireto}' style='background-color: #0d6efd; color: white; padding: 12px 24px; text-decoration: none; font-weight: bold; border-radius: 5px; display: inline-block;'>{textoBotao}</a>                </div>                <hr style='border: 0; border-top: 1px solid #eee;' />                <p style='font-size: 0.8rem; color: #777;'>Este é um email automático gerado pelo Portal Erasmus@IPT.</p>            </div>";

                        foreach (var email in emailsDestino) {
                            string targetEmail = email; // Isolamento de escopo para evitar problemas com threads assíncronas
                            _ = Task.Run(async () => await _servicoEmail.EnviarEmailAsync(targetEmail, assunto, mensagemHtml));
                        }
                    }
                }
                catch (Exception ex) {
                    LogService.RegistarErro("Lógica Email Coordenador AlterarEstado", ex);
                }
                // FIM DO BLOCO DE EMAIL

                return RedirectToAction(nameof(Details), new { id = candidatura.Id });
            }
            catch (Exception ex) {
                LogService.RegistarErro("Coordenator AlterarEstado POST", ex);
                TempData["Erro"] = "Ocorreu um erro ao tentar processar a decisão.";
                return RedirectToAction(nameof(Details), new { id = id });
            }
        }

        // GET: Coordenator/DownloadDocumento
        /// <summary>
        /// Permite ao coordenador descarregar um documento associado a uma candidatura do seu curso.
        /// O acesso é recusado se o documento não pertencer a uma candidatura acessível ao coordenador.
        /// </summary>
        /// <param name="documentoId">Identificador do documento.</param>
        /// <returns>Ficheiro físico para download ou erro de acesso quando aplicável.</returns>
        public async Task<IActionResult> DownloadDocumento(Guid documentoId) {
            try {
                var documento = await _context.Documentos
                .Include(d => d.Candidatura)
                    .ThenInclude(c => c.Curso)
                .FirstOrDefaultAsync(d => d.Id == documentoId);

                // Impede o acesso a documentos de rascunhos privados de alunos.
                if (documento == null || (documento.Candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmConstrucao && documento.Candidatura.OrigemCorrecao != EnumOrigemCorrecao.Coordenador)) {
                    TempData["Erro"] = "O documento procurado não existe ou não está disponível.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                // BLOCO DE SEGURANÇA RGPD 
                // O Coordenador Académico é bloqueado de descarregar comprovativos financeiros
                if (documento.CategoriaFK.Contains("IBAN")) {
                    LogService.RegistarErro("Segurança RGPD", new Exception($"Coordenador {User.Identity?.Name} tentou aceder ao documento financeiro {documentoId}. Acesso negado."));
                    TempData["Erro"] = "Acesso Recusado. Não tens competências para descarregar documentos financeiros.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }
                //FIM DO BLOCO DE SEGURANÇA

                if (!await CoordenadorPodeAcederCandidaturaAsync(documento.Candidatura)) {
                    TempData["Erro"] = "Acesso negado. Esta candidatura não pertence aos teus cursos coordenados.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var caminhoCompleto = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    documento.CaminhoFicheiroSistema
                );

                if (!System.IO.File.Exists(caminhoCompleto)) {
                    TempData["Erro"] = "O ficheiro físico não foi encontrado no servidor.";
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
                LogService.RegistarErro("Coordenator DownloadDocumento GET", ex);
                TempData["Erro"] = "Ocorreu um erro interno ao tentar processar o descarregamento do documento.";
                return RedirectToAction("AcessoRestrito", "Home");
            }
        }

        /// <summary>
        /// Permite ao coordenador adicionar nova documentação técnica ao processo do aluno (ex: Plano de Estudos aprovado).
        /// Verifica constrangimentos temporais (Participant Report) e veda sumariamente categorias financeiras.
        /// </summary>
        /// <param name="candidaturaId">O processo letivo de destino.</param>
        /// <param name="categoriaDocumento">A classificação tipológica do anexo.</param>
        /// <param name="descricaoDocumento">Anotações facultativas à anexação.</param>
        /// <param name="ficheiroDocumento">O payload do ficheiro expedido.</param>
        /// <returns>Reflete a atualização na View de Detalhes.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDocumento(Guid candidaturaId, string categoriaDocumento, string? descricaoDocumento, IFormFile ficheiroDocumento)
        {
            try
            {
                var candidatura = await _context.Candidaturas
                    .Include(c => c.Curso)
                    .FirstOrDefaultAsync(c => c.Id == candidaturaId);

                if (candidatura == null)
                {
                    TempData["Erro"] = "A candidatura indicada não foi encontrada.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (categoriaDocumento == "Participant Report")
                {
                    TempData["Erro"] = "O Participant Report só pode ser submetido na fase pós-mobilidade.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                if (!await CoordenadorPodeAcederCandidaturaAsync(candidatura))
                {
                    TempData["Erro"] = "Acesso negado, a candidatura não pertence aos seus cursos coordenados.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (string.IsNullOrWhiteSpace(categoriaDocumento))
                {
                    TempData["Erro"] = "Tem de selecionar o tipo de documento.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                if (categoriaDocumento.Contains("IBAN", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Erro"] = "O Coordenador não pode adicionar documentos financeiros.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                if (ficheiroDocumento == null || ficheiroDocumento.Length == 0)
                {
                    TempData["Erro"] = "Tem de selecionar um ficheiro.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                if (!FicheiroDocumentoValido(ficheiroDocumento))
                {
                    TempData["Erro"] = "Formato de ficheiro inválido. São permitidos apenas PDF, JPG, PNG ou DOCX.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }


                var categoriaExiste = await _context.CategoriasDocumentos
                    .AnyAsync(c => c.Nome == categoriaDocumento);

                if (!categoriaExiste) {
                    TempData["Erro"] = "A categoria de documento selecionada é inválida.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                var categoriaNormalizada = categoriaDocumento.Trim();

                if (!categoriaNormalizada.Equals("Outro", StringComparison.OrdinalIgnoreCase))
                {
                    var documentoJaExiste = await _context.Documentos
                        .AnyAsync(d =>
                            d.CandidaturaFK == candidaturaId &&
                            d.CategoriaFK == categoriaNormalizada);

                    if (documentoJaExiste)
                    {
                        TempData["Erro"] = "Já existe um documento desta categoria nesta candidatura. Usa a opção Substituir.";
                        return RedirectToAction(nameof(Details), new { id = candidaturaId });
                    }
                }
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrWhiteSpace(userId)) {
                    TempData["Erro"] = "Acesso negado. Não foi possível validar o utilizador autenticado.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var documentoId = Guid.NewGuid();
                var caminhoRelativo = await GuardarFicheiroDocumentoAsync(ficheiroDocumento, candidaturaId, documentoId);

                var documento = new Documento
                {
                    Id = documentoId,
                    CandidaturaFK = candidaturaId,
                    CategoriaFK = categoriaDocumento.Trim(),
                    DescricaoDocumento = descricaoDocumento?.Trim() ?? String.Empty,
                    NomeOriginal = Path.GetFileName(ficheiroDocumento.FileName),
                    CaminhoFicheiroSistema = caminhoRelativo,
                    DataCriacao = DateTime.Now,
                    DataAtualizacao = DateTime.Now,
                    CreatedByFK = userId,
                    UpdatedByFK = userId
                };

                _context.Documentos.Add(documento);
                await _context.SaveChangesAsync();

                TempData["MensagemSucesso"] = "Documento adicionado com sucesso.";
                return RedirectToAction(nameof(Details), new { id = candidaturaId });
            }
            catch (Exception ex)
            {
                LogService.RegistarErro("Coordenator UploadDocumento POST", ex);
                TempData["Erro"] = "Ocorreu um erro ao adicionar o documento.";
                return RedirectToAction(nameof(Details), new { id = candidaturaId });
            }
        }

        /// <summary>
        /// Sobrescreve um documento existente, permitindo correções na documentação sem poluir a Base de Dados.
        /// Aborta a operação se o documento alvo for um IBAN/Comprovativo Bancário.
        /// </summary>
        /// <param name="documentoId">Chave primária do registo a alterar.</param>
        /// <param name="ficheiroDocumento">A nova Stream de dados com o documento corrigido.</param>
        /// <returns>Feedback visual após substituição física e atualização de metadados transacionais.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubstituirDocumento(Guid documentoId, IFormFile ficheiroDocumento)
        {
            try
            {
                var documento = await _context.Documentos
                    .Include(d => d.Candidatura)
                        .ThenInclude(c => c.Curso)
                    .FirstOrDefaultAsync(d => d.Id == documentoId);

                if (documento == null)
                {
                    TempData["Erro"] = "O documento indicado não foi encontrado.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (documento.CategoriaFK.Contains("IBAN", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Erro"] = "O Coordenador não pode substituir documentos financeiros.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (!await CoordenadorPodeAcederCandidaturaAsync(documento.Candidatura))
                {
                    TempData["Erro"] = "Acesso negado. Esta candidatura não pertence aos seus cursos coordenados.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (ficheiroDocumento == null || ficheiroDocumento.Length == 0)
                {
                    TempData["Erro"] = "Tem de selecionar um ficheiro.";
                    return RedirectToAction(nameof(Details), new { id = documento.CandidaturaFK });
                }

                if (!FicheiroDocumentoValido(ficheiroDocumento))
                {
                    TempData["Erro"] = "Formato de ficheiro inválido. São permitidos apenas PDF, JPG, PNG ou DOCX.";
                    return RedirectToAction(nameof(Details), new { id = documento.CandidaturaFK });
                }
                var novoFicheiroId = Guid.NewGuid();

                var novoCaminhoRelativo = await GuardarFicheiroDocumentoAsync(ficheiroDocumento, documento.CandidaturaFK, novoFicheiroId);
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrWhiteSpace(userId)) {
                    TempData["Erro"] = "Acesso negado. Não foi possível validar o utilizador autenticado.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                documento.NomeOriginal = Path.GetFileName(ficheiroDocumento.FileName);
                documento.CaminhoFicheiroSistema = novoCaminhoRelativo;
                documento.DataAtualizacao = DateTime.Now;
                documento.UpdatedByFK = userId;

                _context.Documentos.Update(documento);
                await _context.SaveChangesAsync();

                TempData["MensagemSucesso"] = "Documento substituído com sucesso.";
                return RedirectToAction(nameof(Details), new { id = documento.CandidaturaFK });
            }
            catch (Exception ex)
            {
                LogService.RegistarErro("Coordenator SubstituirDocumento POST", ex);
                TempData["Erro"] = "Ocorreu um erro ao substituir o documento.";
                return RedirectToAction("AcessoRestrito", "Home");
            }
        }

        /// <summary>
        /// Valida se as extensões (.pdf, .png, etc.) e o peso do anexo (<= 10MB) cumprem os requisitos de segurança.
        /// Impede ataques de injeção e o esgotamento do armazenamento (DDoS) no servidor.
        /// </summary>
        /// <param name="ficheiro">A Stream de memória interceptada no pipeline HTTP.</param>
        /// <returns>True se válido, False em caso de violação.</returns>
        private static bool FicheiroDocumentoValido(IFormFile ficheiro) {
            var extensoesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".docx" };
            var extensao = Path.GetExtension(ficheiro.FileName).ToLowerInvariant();

            return extensoesPermitidas.Contains(extensao) && ficheiro.Length <= 10 * 1024 * 1024;
        }

        /// <summary>
        /// Processa a escrita do binário submetido para a partição física do servidor.
        /// Organiza estruturalmente a diretoria em /wwwroot/uploads/candidaturas/{GUID}.
        /// </summary>
        /// <param name="ficheiro">O documento recebido do cliente.</param>
        /// <param name="candidaturaId">O GUID do processo a fim de criar uma pasta isolada.</param>
        /// <param name="documentoId">O GUID a adotar como nome do ficheiro para não sobrepor outros anexos da mesma candidatura.</param>
        /// <returns>A *String* com a path relativa necessária para gravar e indexar na Base de Dados.</returns>
        private static async Task<string> GuardarFicheiroDocumentoAsync(IFormFile ficheiro, Guid candidaturaId, Guid documentoId)
        {
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

            await using (var stream = new FileStream(caminhoCompleto, FileMode.Create))
            {
                await ficheiro.CopyToAsync(stream);
            }

            return $"uploads/candidaturas/{candidaturaId}/{nomeFisico}";
        }
    }
}