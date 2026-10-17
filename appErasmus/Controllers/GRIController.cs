using appErasmus.Data;
using appErasmus.Models;
using appErasmus.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace appErasmus.Controllers {
    /// <summary>
    /// Controller responsável pela área do Gabinete de Relações Internacionais.
    /// Permite consultar candidaturas submetidas, visualizar detalhes, descarregar documentos
    /// e executar a decisão final do processo de mobilidade.
    /// </summary>
    [Authorize(Roles = "GRI")]
    public class GRIController : Controller {
        private readonly ApplicationDbContext _context;
        private readonly IServicoEmail _servicoEmail;

        /// <summary>
        /// Inicializa o controller com o contexto da base de dados.
        /// </summary>
        /// <param name="context">Contexto da aplicação usado para aceder às candidaturas e documentos.</param>
        /// <param name="servicoEmail">Serviço de envio de emails.</param>
        public GRIController(ApplicationDbContext context, IServicoEmail servicoEmail) {
            _context = context;
            _servicoEmail = servicoEmail;
        }

        // GET: GRI
        /// <summary>
        /// Lista as candidaturas visíveis para o GRI.
        /// Exclui candidaturas ainda em rascunho, por ainda não terem sido submetidas.
        /// </summary>
        /// <returns>View com a lista de candidaturas disponíveis para análise do GRI.</returns>
        public async Task<IActionResult> Index() {
            try {
                var candidaturas = await _context.Candidaturas
                .Include(c => c.Aluno)
                .Include(c => c.Curso)
                    .ThenInclude(curso => curso.Escola)
                .Include(c => c.Curso)
                    .ThenInclude(curso => curso.GrauCurso)
                .Include(c => c.Linguas)
                .Where(c => c.EstadoCandidatura != EnumEstadoCandidatura.EmConstrucao || c.OrigemCorrecao != EnumOrigemCorrecao.None)
                .OrderByDescending(c => c.DataSubmissao)
                .ToListAsync();

                return View(candidaturas);
            }
            catch (Exception ex) {
                LogService.RegistarErro("GRI Index GET", ex);
                return View(new List<Candidatura>());
            }
        }

        // GET: GRI/Details/5
        /// <summary>
        /// Mostra os detalhes completos de uma candidatura submetida,
        /// incluindo dados do aluno, curso, universidades, línguas e documentos anexados.
        /// </summary>
        /// <param name="id">Identificador da candidatura.</param>
        /// <returns>View de detalhe da candidatura ou NotFound quando a candidatura não pode ser consultada.</returns>
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
                .Include(c => c.Documentos)
                    .ThenInclude(d => d.Categoria)
                .Include(c => c.Documentos)
                    .ThenInclude(d => d.CreatedBy)
                .Include(c => c.Documentos)
                    .ThenInclude(d => d.UpdatedBy)
                .Include(c => c.CandidaturasUniversidades)
                    .ThenInclude(cu => cu.Universidade)
                .FirstOrDefaultAsync(c => c.Id == id);

                // Impede acesso a candidaturas inexistentes ou ainda em rascunho privado do aluno.
                if (candidatura == null || (candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmConstrucao && candidatura.OrigemCorrecao == EnumOrigemCorrecao.None)) {
                    TempData["Erro"] = "O processo procurado não existe ou não está disponível para o GRI.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }
                var categoriasJaExistentes = await _context.Documentos
                    .Where(d => d.CandidaturaFK == candidatura.Id)
                    .Select(d => d.CategoriaFK)
                    .ToListAsync();

                ViewBag.CategoriasDocumento = await _context.CategoriasDocumentos
                .Where(c => c.Nome != "Participant Report")
                    .Where(c =>
                        !categoriasJaExistentes.Contains(c.Nome) ||
                        c.Nome == "Outro")
                    .OrderBy(c => c.Nome)
                    .ToListAsync();
                return View(candidatura);
            }
            catch (Exception ex) {
                LogService.RegistarErro("GRI Details GET", ex);
                TempData["Erro"] = "Erro interno ao carregar os detalhes da candidatura.";
                return RedirectToAction("AcessoRestrito", "Home");
            }
        }

        // POST: GRI/AlterarEstado
        /// <summary>
        /// Altera o estado de uma candidatura na fase de decisão do GRI.
        /// Permite aceitar a candidatura, rejeitar definitivamente ou devolver ao aluno para correção.
        /// </summary>
        /// <param name="id">Identificador da candidatura.</param>
        /// <param name="acao">Ação executada pelo GRI.</param>
        /// <param name="motivoRejeicao">Motivo da rejeição ou devolução.</param>
        /// <param name="permiteCorrecao">Indica se o aluno pode corrigir e voltar a submeter.</param>
        /// <returns>Redirecionamento para os detalhes da candidatura.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AlterarEstado(Guid id, string acao, string? motivoRejeicao, bool permiteCorrecao = false) {
            try {
                var candidatura = await _context.Candidaturas.Include(c => c.Aluno).FirstOrDefaultAsync(c => c.Id == id);

                // Garante que o GRI não altera candidaturas inexistentes ou ainda em rascunho.
                if (candidatura == null || candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmConstrucao) {
                    TempData["Erro"] = "O processo indicado não foi encontrado ou encontra-se em modo de rascunho.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                // Garante que o GRI só decide candidaturas que chegaram à Análise Inicial (4) OU Análise Final (8).
                if (candidatura.EstadoCandidatura != EnumEstadoCandidatura.EmAnaliseGRI &&
                    candidatura.EstadoCandidatura != EnumEstadoCandidatura.EmAnaliseFinalGRI) {
                    TempData["Erro"] = "Esta candidatura já não se encontra na fase de decisão regulamentar do GRI.";
                    return RedirectToAction(nameof(Details), new { id = candidatura.Id });
                }

                switch (acao) {
                    case "AprovarGRI":
                        // Garante que esta ação só funciona na Fase Inicial de aprovação
                        if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmAnaliseGRI) {
                            candidatura.EstadoCandidatura = EnumEstadoCandidatura.Aceite;
                            candidatura.MotivoRejeicao = string.Empty;
                            candidatura.PermiteCorrecao = false;
                            candidatura.OrigemCorrecao = EnumOrigemCorrecao.None;
                            candidatura.DataValidacao = DateTime.Now;
                            TempData["MensagemSucesso"] = "Candidatura aprovada com sucesso! O aluno pode iniciar a mobilidade.";
                        }
                        break;

                    case "FinalizarProcesso":
                        // Garante que esta ação só funciona na Fase Pós-Mobilidade
                        if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmAnaliseFinalGRI) {
                            candidatura.EstadoCandidatura = EnumEstadoCandidatura.Finalizado; // Estado 99
                            candidatura.DataValidacao = DateTime.Now;
                            candidatura.PermiteCorrecao = false;
                            candidatura.OrigemCorrecao = EnumOrigemCorrecao.None;
                            candidatura.MotivoRejeicao = string.Empty; // Limpa eventuais avisos de devolução

                            TempData["MensagemSucesso"] = "Documentação validada! O processo de mobilidade foi encerrado com sucesso.";
                        }
                        break;

                    case "RejeitarGRI":
                        // Rejeições e devoluções exigem uma justificação visível para o aluno.
                        // Esta ação é comum tanto à Análise Inicial como à Análise Final (Devolver documentos errados).
                        if (string.IsNullOrWhiteSpace(motivoRejeicao)) {
                            return RedirectToAction(nameof(Details), new { id = candidatura.Id });
                        }

                        candidatura.MotivoRejeicao = motivoRejeicao.Trim();
                        candidatura.DataValidacao = DateTime.Now;

                        if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmAnaliseFinalGRI) {
                            candidatura.EstadoCandidatura = EnumEstadoCandidatura.DevolvidaParaCorrecao;
                            candidatura.PermiteCorrecao = true;
                            candidatura.OrigemCorrecao = EnumOrigemCorrecao.GRI;
                        } else if (permiteCorrecao) {
                            candidatura.EstadoCandidatura = EnumEstadoCandidatura.DevolvidaParaCorrecao;
                            candidatura.PermiteCorrecao = true;
                            candidatura.OrigemCorrecao = EnumOrigemCorrecao.GRI;
                        } else {
                            candidatura.EstadoCandidatura = EnumEstadoCandidatura.RejeitadaNaoAceite;
                            candidatura.PermiteCorrecao = false;
                            candidatura.OrigemCorrecao = EnumOrigemCorrecao.None;
                        }

                        break;

                    default:
                        return RedirectToAction(nameof(Details), new { id = candidatura.Id });
                }

                await _context.SaveChangesAsync();

                // INÍCIO DO BLOCO DE EMAIL DO GRI
                try {
                    string emailDestino = candidatura.Aluno?.Email ?? string.Empty; // Daqui do GRI tudo vai para o aluno
                    string linkDireto = Url.Action("Details", "Candidaturas", new { id = candidatura.Id }, protocol: Request.Scheme) ?? string.Empty;
                    string assunto = "";
                    string tituloCorpo = "";
                    string textoCorpo = "";
                    string textoBotao = "Ver Processo";

                    if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.Aceite) {
                        assunto = "🎉 [Portal Erasmus] Candidatura Aceite!";
                        tituloCorpo = "Processo de Mobilidade Aprovado";
                        textoCorpo = "Parabéns! O teu processo foi validado pelo GRI e a tua mobilidade está oficialmente aceite. Desejamos-te uma excelente viagem!";
                    }
                    else if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.Finalizado) {
                        assunto = "🎓 [Portal Erasmus] Processo Concluído";
                        tituloCorpo = "Mobilidade Erasmus Finalizada";
                        textoCorpo = "Os teus documentos finais de regresso foram validados com sucesso. O teu processo de mobilidade Erasmus encontra-se agora formalmente encerrado.";
                    }
                    else if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.DevolvidaParaCorrecao) {
                        if (candidatura.OrigemCorrecao == EnumOrigemCorrecao.GRI) {
                            linkDireto = Url.Action("Details", "Candidaturas", new { id = candidatura.Id }, protocol: Request.Scheme) ?? string.Empty;
                        } else {
                            linkDireto = Url.Action("Edit", "Candidaturas", new { id = candidatura.Id }, protocol: Request.Scheme) ?? string.Empty;
                        }
                        // Assunto claro, mas que usa "Revisão" em vez de "Devolução" ou "Correção"
                        assunto = $"[Portal Erasmus] Revisão de Dados Necessária - {candidatura.Aluno?.NomeCompleto}";
                        tituloCorpo = "Análise Técnica ao Processo";

                        // O texto explica que algo precisa de ser afinado para o processo avançar
                        textoCorpo = $"A tua candidatura foi analisada pelos serviços do GRI, mas existem alguns detalhes que precisam de ser revistos ou clarificados para podermos dar seguimento ao processo:<br><br><em>\"{candidatura.MotivoRejeicao}\"</em>";

                        textoBotao = "Rever e Atualizar Processo";
                    }
                    else if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.RejeitadaNaoAceite) {
                        assunto = assunto = $"[Portal Erasmus] Decisão Final: Processo Cancelado - {candidatura.Aluno?.NomeCompleto}";
                        tituloCorpo = "Processo Rejeitado/Cancelado";
                        textoCorpo = $"O teu processo de mobilidade foi formalmente recusado ou cancelado pelos serviços do GRI. Motivo indicado:<br><br><em>\"{candidatura.MotivoRejeicao}\"</em>";
                    }

                    if (!string.IsNullOrEmpty(assunto)) {
                        string mensagemHtml = $@"
                        <div style='font-family: sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #eee; padding: 20px; border-radius: 8px;'>
                            <h2 style='color: #0d6efd;'>{tituloCorpo}</h2>
                            <p>{textoCorpo}</p>
                            <div style='margin: 30px 0; text-align: center;'>
                                <a href='{linkDireto}' style='background-color: #0d6efd; color: white; padding: 12px 24px; text-decoration: none; font-weight: bold; border-radius: 5px; display: inline-block;'>{textoBotao}</a>
                            </div>
                            <hr style='border: 0; border-top: 1px solid #eee;' />
                            <p style='font-size: 0.8rem; color: #777;'>Este é um email automático gerado pelo Portal Erasmus@IPT.</p>
                        </div>";

                        _ = Task.Run(async () => await _servicoEmail.EnviarEmailAsync(emailDestino, assunto, mensagemHtml));
                    }
                }
                catch (Exception ex) {
                    LogService.RegistarErro("Lógica Email GRI AlterarEstado", ex);
                }
                // FIM DO BLOCO DE EMAIL

                return RedirectToAction(nameof(Details), new { id = candidatura.Id });
            }
            catch (Exception ex) {
                LogService.RegistarErro("GRI AlterarEstado POST", ex);
                TempData["Erro"] = "Ocorreu um erro crítico ao processar a decisão.";
                return RedirectToAction(nameof(Details), new { id = id });
            }
        }

        // GET: GRI/DownloadDocumento
        /// <summary> 
        /// Lê o ficheiro físico no servidor e devolve-o ao browser para download/visualização segura.
        /// Possui bloqueios contra o descarregamento de documentos de candidaturas não finalizadas ou não submetidas.
        /// </summary>
        /// <param name="documentoId">A chave primária do documento na base de dados.</param>
        /// <returns>O ficheiro formatado via PhysicalFile ou um redirecionamento de segurança em caso de falha.</returns>
        public async Task<IActionResult> DownloadDocumento(Guid documentoId) {
            try {
                var documento = await _context.Documentos
                .Include(d => d.Candidatura)
                .FirstOrDefaultAsync(d => d.Id == documentoId);

                if (documento == null) {
                    TempData["Erro"] = "O arquivo solicitado não foi localizado na Base de Dados.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                // Impede acesso a documentos de candidaturas ainda em rascunho privado do aluno.
                if (documento.Candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmConstrucao && documento.Candidatura.OrigemCorrecao == EnumOrigemCorrecao.None) {
                    TempData["Erro"] = "Não é permitido aceder a arquivos de candidaturas sob modo rascunho.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var caminhoCompleto = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    documento.CaminhoFicheiroSistema
                );

                if (!System.IO.File.Exists(caminhoCompleto)) {
                    TempData["Erro"] = "O ficheiro físico associado a este registo não foi localizado no servidor.";
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
                LogService.RegistarErro("GRI DownloadDocumento GET", ex);
                TempData["Erro"] = "Erro interno no servidor ao tentar descarregar o ficheiro.";
                return RedirectToAction("AcessoRestrito", "Home");
            }
        }

        /// <summary>
        /// Permite ao corpo administrativo do GRI anexar novos documentos a um processo.
        /// Valida integridade do ficheiro e injeta rastreabilidade transacional (CreatedBy).
        /// </summary>
        /// <param name="candidaturaId">ID da candidatura que receberá o anexo.</param>
        /// <param name="categoriaDocumento">Categoria normativa (ex: Reconhecimento Académico, Ficha de Seleção).</param>
        /// <param name="descricaoDocumento">Contextualização em texto livre (útil em caso de categoria "Outro").</param>
        /// <param name="ficheiroDocumento">A stream física do ficheiro contendo o *payload* do documento.</param>
        /// <returns>Redireciona para o dossier do processo com o histórico de anexos atualizado.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDocumento(Guid candidaturaId, string categoriaDocumento, string? descricaoDocumento, IFormFile ficheiroDocumento)
        {
            try
            {
                var candidatura = await _context.Candidaturas
                    .FirstOrDefaultAsync(c => c.Id == candidaturaId);

                if (candidatura == null) {
                    TempData["Erro"] = "A candidatura indicada não foi encontrada.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }
                
                if (categoriaDocumento == "Participant Report") {
                    TempData["Erro"] = "O Participant Report só pode ser submetido na fase pós-mobilidade.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                if (candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmConstrucao && candidatura.OrigemCorrecao == EnumOrigemCorrecao.None) {
                    TempData["Erro"] = "Não é permitido adicionar documentos a candidaturas em rascunho privado.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (string.IsNullOrWhiteSpace(categoriaDocumento))
                {
                    TempData["Erro"] = "Tem de selecionar o tipo de documento.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                var categoriaNormalizada = categoriaDocumento.Trim();

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
                    .AnyAsync(c => c.Nome == categoriaNormalizada);

                if (!categoriaExiste)
                {
                    TempData["Erro"] = "A categoria de documento selecionada é inválida.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                var documentoJaExiste = await _context.Documentos
                    .AnyAsync(d =>
                        d.CandidaturaFK == candidaturaId &&
                        d.CategoriaFK == categoriaNormalizada);

                if (documentoJaExiste &&
                    !categoriaNormalizada.Equals("Outro", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Erro"] = "Já existe um documento desta categoria nesta candidatura. Usa a opção Substituir.";
                    return RedirectToAction(nameof(Details), new { id = candidaturaId });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrWhiteSpace(userId))
                {
                    TempData["Erro"] = "Acesso negado. Não foi possível validar o utilizador autenticado.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var documentoId = Guid.NewGuid();
                var caminhoRelativo = await GuardarFicheiroDocumentoAsync(ficheiroDocumento, candidaturaId, documentoId);

                var documento = new Documento
                {
                    Id = documentoId,
                    CandidaturaFK = candidaturaId,
                    CategoriaFK = categoriaNormalizada,
                    DescricaoDocumento = descricaoDocumento?.Trim() ?? string.Empty,
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
                LogService.RegistarErro("GRI UploadDocumento POST", ex);
                TempData["Erro"] = "Ocorreu um erro ao adicionar o documento.";
                return RedirectToAction(nameof(Details), new { id = candidaturaId });
            }
        }

        /// <summary>
        /// Permite ao GRI executar "Overwrites" a ficheiros defeituosos ou assinados.
        /// Mantém os metadados do documento original intactos, atualizando apenas a assinatura física
        /// no sistema de ficheiros e injetando um registo de autoria transacional na base de dados (UpdatedBy).
        /// </summary>
        /// <param name="documentoId">Identificador do Registo-Alvo a substituir.</param>
        /// <param name="ficheiroDocumento">O novo binário do documento a anexar.</param>
        /// <returns>Redireciona para o dossier do processo com o documento corrigido visível.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubstituirDocumento(Guid documentoId, IFormFile ficheiroDocumento)
        {
            try
            {
                var documento = await _context.Documentos
                    .Include(d => d.Candidatura)
                    .FirstOrDefaultAsync(d => d.Id == documentoId);

                if (documento == null)
                {
                    TempData["Erro"] = "O documento indicado não foi encontrado.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                if (documento.Candidatura.EstadoCandidatura == EnumEstadoCandidatura.EmConstrucao &&
                    documento.Candidatura.OrigemCorrecao == EnumOrigemCorrecao.None)
                {
                    TempData["Erro"] = "Não é permitido substituir documentos de candidaturas em rascunho privado.";
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

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrWhiteSpace(userId))
                {
                    TempData["Erro"] = "Acesso negado. Não foi possível validar o utilizador autenticado.";
                    return RedirectToAction("AcessoRestrito", "Home");
                }

                var novoFicheiroId = Guid.NewGuid();

                var novoCaminhoRelativo = await GuardarFicheiroDocumentoAsync(
                    ficheiroDocumento,
                    documento.CandidaturaFK,
                    novoFicheiroId
                );

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
                LogService.RegistarErro("GRI SubstituirDocumento POST", ex);
                TempData["Erro"] = "Ocorreu um erro ao substituir o documento.";
                return RedirectToAction("AcessoRestrito", "Home");
            }
        }

        /// <summary>
        /// Rotina auxiliar de segurança que avalia se a integridade, peso e extensão do 
        /// ficheiro em transferência estão dentro das margens normativas estipuladas.
        /// O limite base parametrizado e hardcoded neste sistema é de 10 MB.
        /// </summary>
        /// <param name="ficheiro">A Stream de memória do ficheiro submetido pelo cliente.</param>
        /// <returns>True se válido e limpo, False se perigoso, não-suportado ou demasiado grande.</returns>
        private static bool FicheiroDocumentoValido(IFormFile ficheiro)
        {
            var extensoesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".docx" };
            var extensao = Path.GetExtension(ficheiro.FileName).ToLowerInvariant();

            return extensoesPermitidas.Contains(extensao) && ficheiro.Length <= 10 * 1024 * 1024;
        }

        /// <summary>
        /// Algoritmo utilitário que garante as operações de I/O em disco no servidor.
        /// Gera caminhos virtuais e aloca o ficheiro estático dentro do namespace "/wwwroot/uploads",
        /// assegurando organização do tipo "/uploads/candidaturas/{GUID Candidatura}/{GUID Documento}.pdf"
        /// de modo a prevenir colisões em nomes e facilitar limpeza sistemática no futuro.
        /// </summary>
        /// <param name="ficheiro">Arquivo em buffer aguardando I/O flush.</param>
        /// <param name="candidaturaId">ID pai para agregação em diretório (Pasta da Candidatura).</param>
        /// <param name="documentoId">ID filho para alocação do nome (Prevenção de overwrite indevido).</param>
        /// <returns>Retorna a string virtual / relativa necessária para ser acomodada na Base de Dados e navegável.</returns>
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