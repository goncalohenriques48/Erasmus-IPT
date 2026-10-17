using System;
using System.IO;

namespace appErasmus.Services {
    /// <summary>
    /// Serviço centralizado para persistência de logs de erro em produção.
    /// Grava as exceções diretamente no disco do servidor para garantir a auditoria
    /// mesmo em cenários de indisponibilidade da Base de Dados.
    /// </summary>
    public static class LogService {
        private static readonly string _caminhoLog = Path.Combine(Directory.GetCurrentDirectory(), "logs", "error_log.txt");

        public static void RegistarErro(string contexto, Exception ex) {
            try {
                // Extrai a pasta e previne possíveis valores nulos
                string? pasta = Path.GetDirectoryName(_caminhoLog);

                // Se a extração falhar, não tenta sequer criar a pasta para não causar NullReferenceException
                if (!string.IsNullOrEmpty(pasta) && !Directory.Exists(pasta)) {
                    Directory.CreateDirectory(pasta);
                }

                string blocoErro = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [CONTEXTO: {contexto.ToUpper()}]" + Environment.NewLine +
                                   $"Mensagem: {ex.Message}" + Environment.NewLine +
                                   $"Stack Trace: {ex.StackTrace}" + Environment.NewLine +
                                   $"{new string('-', 80)}" + Environment.NewLine;

                File.AppendAllText(_caminhoLog, blocoErro);
            }
            catch {
                // Failsafe silencioso: se o disco estiver cheio ou trancado, ignora
            }
        }
    }
}