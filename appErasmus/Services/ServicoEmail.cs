using System.Net;
using System.Net.Mail;
using System.Diagnostics;

namespace appErasmus.Services {
    /// <summary>
    /// Interface que define o contrato para o serviço de envio de emails da aplicação.
    /// Permite a injeção de dependência (Dependency Injection) para desacoplar a lógica de negócio da infraestrutura física de email.
    /// </summary>
    public interface IServicoEmail {
        /// <summary>
        /// Envia uma mensagem de email de forma assíncrona.
        /// </summary>
        /// <param name="emailDestino">O endereço de email do destinatário.</param>
        /// <param name="assunto">O título/assunto que aparecerá na caixa de entrada do destinatário.</param>
        /// <param name="mensagemHtml">O corpo da mensagem formatado em HTML para suportar layouts ricos.</param>
        /// <returns>Uma tarefa (Task) que representa a operação assíncrona de envio.</returns>
        Task EnviarEmailAsync(string emailDestino, string assunto, string mensagemHtml);
    }

    /// <summary>
    /// Implementação concreta do serviço de email utilizando o protocolo SMTP (Simple Mail Transfer Protocol).
    /// As credenciais e definições do servidor são consumidas dinamicamente a partir do ficheiro de configuração global da aplicação.
    /// </summary>
    public class ServicoEmail : IServicoEmail {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Inicializa uma nova instância do serviço de email, injetando as configurações do ambiente.
        /// </summary>
        /// <param name="configuration">A interface de configuração (appsettings.json) fornecida pelo container de injeção de dependências do .NET.</param>
        public ServicoEmail(IConfiguration configuration) {
            _configuration = configuration;
        }

        /// <summary>
        /// Constrói e despacha um email através do cliente SMTP configurado.
        /// Estabelece uma ligação segura (SSL) e faz a autenticação silenciosa com os servidores de email (ex: Gmail).
        /// </summary>
        /// <param name="emailDestino">O endereço de email do utilizador que vai receber a notificação.</param>
        /// <param name="assunto">O assunto do email.</param>
        /// <param name="mensagemHtml">O conteúdo integral da mensagem em formato HTML.</param>
        public async Task EnviarEmailAsync(string emailDestino, string assunto, string mensagemHtml) {
            // Vai buscar as configurações ao appsettings.json
            var emailOrigem = _configuration["EmailSettings:SenderEmail"];
            var nomeExibicao = _configuration["EmailSettings:SenderName"];
            var senhaApp = _configuration["EmailSettings:AppPassword"];
            var hostSmtp = _configuration["EmailSettings:SmtpServer"];
            var portSmtp = int.Parse(_configuration["EmailSettings:Port"] ?? "587");

            var clienteSmtp = new SmtpClient(hostSmtp, portSmtp) {
                Credentials = new NetworkCredential(emailOrigem, senhaApp),
                EnableSsl = true
            };

            var mailMessage = new MailMessage {
                From = new MailAddress(emailOrigem!, nomeExibicao),
                Subject = assunto,
                Body = mensagemHtml,
                IsBodyHtml = true
            };

            mailMessage.To.Add(emailDestino);

            try {
                await clienteSmtp.SendMailAsync(mailMessage);
                Debug.WriteLine($"[GMAIL SUCESSO] Email enviado para: {emailDestino}");
            }
            catch (Exception ex) {
                Debug.WriteLine($"[ERRO GMAIL SMTP] Falha ao enviar para {emailDestino}: {ex.Message}");
            }
        }
    }
}