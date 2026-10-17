using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace appErasmus.Models.Validations {

    /// <summary>
    /// Validação customizada para IBANs internacionais.
    /// Verifica a formatação geral e aplica regras rigorosas de comprimento
    /// e prefixos obrigatórios consoante o país de origem da conta.
    /// </summary>
    public class ValidarIBANAttribute : ValidationAttribute {

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) {

            // Se estiver vazio, não faz nada. O [Required] é que tem de travar os nulos.
            if (value == null || string.IsNullOrWhiteSpace(value.ToString())) {
                return ValidationResult.Success;
            }

            // Remove os espaços em branco que o utilizador possa ter posto e passa a maiúsculas
            string iban = value.ToString()!.Replace(" ", "").ToUpper();

            // 1. REGRA GERAL MUNDIAL: 2 Letras + 2 Dígitos + Alfanumérico (máx 30)
            if (!Regex.IsMatch(iban, @"^[A-Z]{2}\d{2}[A-Z0-9]+$")) {
                return new ValidationResult("O formato estrutural do IBAN é inválido (apenas letras e números são permitidos).");
            }

            // 2. REGRAS ESPECÍFICAS POR PAÍS
            string pais = iban.Substring(0, 2);

            switch (pais) {
                case "PT": // Portugal
                    if (!iban.StartsWith("PT50")) {
                        return new ValidationResult("Os IBANs de contas portuguesas começam obrigatoriamente por 'PT50'.");
                    }
                    if (iban.Length != 25) {
                        return new ValidationResult($"Um IBAN português tem de ter exatamente 25 caracteres. O que inseriste tem {iban.Length}.");
                    }
                    break;

                case "BR": // Brasil
                    if (iban.Length != 29) {
                        return new ValidationResult($"Um IBAN brasileiro tem de ter exatamente 29 caracteres. O que inseriste tem {iban.Length}.");
                    }
                    break;

                case "PL": // Polónia
                    if (iban.Length != 28) {
                        return new ValidationResult($"Um IBAN polaco tem de ter exatamente 28 caracteres. O que inseriste tem {iban.Length}.");
                    }
                    break;

                case "ES": // Espanha
                    if (iban.Length != 24) {
                        return new ValidationResult($"Um IBAN espanhol tem de ter exatamente 24 caracteres. O que inseriste tem {iban.Length}.");
                    }
                    break;
            }

            // Se chegou até aqui e não disparou nenhum aviso, é válido!
            return ValidationResult.Success;
        }
    }
}