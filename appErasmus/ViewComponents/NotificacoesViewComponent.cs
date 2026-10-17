using appErasmus.Data;
using appErasmus.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace appErasmus.ViewComponents {
    /// <summary>
    /// Modelo de dados auxiliar (Data Transfer Object - DTO) utilizado para encapsular 
    /// a informação de uma notificação antes de a enviar para ser renderizada na View.
    /// É um modelo leve ("POCO") desenhado especificamente para transporte de dados na UI.
    /// </summary>
    public class AlertaNotificacao {
        /// <summary>
        /// O texto descritivo do alerta a ser lido pelo utilizador (ex: "A tua candidatura foi aceite").
        /// </summary>
        public string Mensagem { get; set; } = string.Empty;

        /// <summary>
        /// O caminho de encaminhamento (URL relativo ou absoluto) para onde o utilizador 
        /// é redirecionado ao clicar na notificação.
        /// </summary>
        public string Link { get; set; } = string.Empty;

        /// <summary>
        /// A classe CSS do ícone (ex: classes do Bootstrap Icons como 'bi-check-circle-fill') 
        /// a ser exibido junto ao texto da notificação.
        /// </summary>
        public string Icone { get; set; } = string.Empty;

        /// <summary>
        /// A classe CSS utilitária (ex: 'text-danger', 'text-success') para dar cor contextual 
        /// ao ícone ou texto da notificação, refletindo a urgência ou o estado da mesma.
        /// </summary>
        public string CorText { get; set; } = string.Empty;
    }

    /// <summary>
    /// Componente de visualização (ViewComponent) responsável por gerar notificações dinâmicas e contextuais 
    /// na interface global da aplicação (ex: no menu de navegação de topo/sino de alertas).
    /// Avalia as permissões (Roles) do utilizador autenticado e interage com a base de dados para verificar 
    /// ações pendentes no Backoffice ou o estado atual das candidaturas do estudante.
    /// </summary>
    public class NotificacoesViewComponent : ViewComponent {
        private readonly ApplicationDbContext _context;

        /// <summary>
        /// Inicializa uma nova instância do componente, estabelecendo a ligação à base de dados.
        /// </summary>
        /// <param name="context">O contexto do Entity Framework injetado via Dependency Injection.</param>
        public NotificacoesViewComponent(ApplicationDbContext context) {
            _context = context;
        }

        /// <summary>
        /// Executa a lógica de negócio principal do componente de forma assíncrona.
        /// Analisa o perfil do utilizador (Coordenador Erasmus, GRI ou Aluno) e compila uma lista 
        /// de alertas estritamente direcionada às necessidades ou tarefas pendentes desse utilizador.
        /// </summary>
        /// <returns>
        /// Retorna um objeto <see cref="IViewComponentResult"/> contendo a View a ser renderizada, 
        /// injetando-lhe a coleção de <see cref="AlertaNotificacao"/> gerada.
        /// </returns>
        public async Task<IViewComponentResult> InvokeAsync() {
            var alertas = new List<AlertaNotificacao>();
            var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId)) {
                return View(alertas);
            }

            // --- 1. NOTIFICAÇÕES PARA O COORDENADOR ---
            if (User.IsInRole("CoordenadorErasmus")) {
                int pendentesCoord = await _context.Candidaturas
                    .CountAsync(c => c.EstadoCandidatura == EnumEstadoCandidatura.Submetida
                                  && c.Curso.CoordenadorFK == userId);

                if (pendentesCoord > 0) {
                    alertas.Add(new AlertaNotificacao {
                        Mensagem = $"Tem {pendentesCoord} candidaturas por avaliar.",
                        Link = "~/Coordenator/Index",
                        Icone = "bi-person-lines-fill",
                        CorText = "text-warning"
                    });
                }
            }

            // --- 2. NOTIFICAÇÕES PARA O GRI ---
            if (User.IsInRole("GRI")) {
                int pendentesGRI = await _context.Candidaturas
                    .CountAsync(c => c.EstadoCandidatura == EnumEstadoCandidatura.EmAnaliseGRI
                                  || c.EstadoCandidatura == EnumEstadoCandidatura.EmAnaliseFinalGRI);

                if (pendentesGRI > 0) {
                    alertas.Add(new AlertaNotificacao {
                        Mensagem = $"Tem {pendentesGRI} processos para aprovação/finalização.",
                        Link = "~/GRI/Index",
                        Icone = "bi-award",
                        CorText = "text-primary"
                    });
                }
            }

            // --- 3. NOTIFICAÇÕES PARA O ALUNO ---
            var minhaCandidatura = await _context.Candidaturas
                .OrderByDescending(c => c.DataSubmissao)
                .FirstOrDefaultAsync(c => c.AlunoFK == userId);

            if (minhaCandidatura != null) {
                if (minhaCandidatura.EstadoCandidatura == EnumEstadoCandidatura.DevolvidaParaCorrecao) {
                    alertas.Add(new AlertaNotificacao {
                        Mensagem = " A tua candidatura foi devolvida para correção.",
                        Link = $"~/Candidaturas/Details/{minhaCandidatura.Id}",
                        Icone = "bi-arrow-counterclockwise",
                        CorText = "text-info"
                    });
                }
                else if (minhaCandidatura.EstadoCandidatura == EnumEstadoCandidatura.RejeitadaCoordenador ||
                         minhaCandidatura.EstadoCandidatura == EnumEstadoCandidatura.RejeitadaNaoAceite) {
                    alertas.Add(new AlertaNotificacao {
                        Mensagem = "❌ O teu processo de mobilidade foi rejeitado.",
                        Link = $"~/Candidaturas/Details/{minhaCandidatura.Id}",
                        Icone = "bi-x-circle-fill",
                        CorText = "text-danger"
                    });
                }
                else if (minhaCandidatura.EstadoCandidatura == EnumEstadoCandidatura.Aceite) {
                    alertas.Add(new AlertaNotificacao {
                        Mensagem = "🎉 Parabéns! A tua candidatura foi aceite.",
                        Link = $"~/Candidaturas/Details/{minhaCandidatura.Id}",
                        Icone = "bi-check-circle-fill",
                        CorText = "text-success"
                    });
                }
        
                else if (minhaCandidatura.EstadoCandidatura == EnumEstadoCandidatura.Finalizado) {
                    alertas.Add(new AlertaNotificacao {
                        Mensagem = "🎓 Parabéns! O teu processo Erasmus foi concluído.",
                        Link = $"~/Candidaturas/Details/{minhaCandidatura.Id}",
                        Icone = "bi-patch-check-fill",
                        CorText = "text-success"
                    });
                }
                else if (minhaCandidatura.EstadoCandidatura == EnumEstadoCandidatura.Submetida ||
                         minhaCandidatura.EstadoCandidatura == EnumEstadoCandidatura.EmAnaliseGRI) {
                    if (!User.IsInRole("CoordenadorErasmus") && !User.IsInRole("GRI") && !User.IsInRole("Admin")) {
                        alertas.Add(new AlertaNotificacao {
                            Mensagem = "A tua candidatura encontra-se em análise.",
                            Link = $"~/Candidaturas/Details/{minhaCandidatura.Id}",
                            Icone = "bi-hourglass-split",
                            CorText = "text-warning"
                        });
                    }
                }
            }

            return View(alertas);
        }
    }
}