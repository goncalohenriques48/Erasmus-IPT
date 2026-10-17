using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace appErasmus.Models {
    /// <summary>
    /// Classe que representa uma Instituição de Ensino Superior parceira do IPT.
    /// NOTA DE ARQUITETURA: Os registos desta tabela são gerados e atualizados automaticamente através
    /// da importação de um ficheiro XML (Seed Data). Esta entidade é essencialmente de leitura 
    /// para os alunos durante o processo de escolha (limitado a 3 preferências).
    /// </summary>
    public class Universidade {
        /// <summary>
        /// Código Erasmus da universidade (chave primária). Ex: P TOMAR01.
        /// Importado via XML. A anotação DatabaseGeneratedOption.None impede o SQL de gerar um ID numérico.
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Display(Name = "Código Erasmus")]
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        public string ErasmusCode { get; set; } = string.Empty;

        /// <summary>
        /// Nome oficial da instituição de destino (Importado via XML).
        /// </summary>
        [Required(ErrorMessage = "O {0} da universidade é de preenchimento obrigatório.")]
        [Display(Name = "Nome da Instituição")]
        public string Nome { get; set; } = string.Empty;

        /// <summary>
        /// Morada oficial da instituição parceira (Importado via XML).
        /// </summary>
        [Required(ErrorMessage = "A {0} é de preenchimento obrigatório.")]
        [Display(Name = "Morada")]
        public string Morada { get; set; } = string.Empty;

        /// <summary>
        /// Localidade (cidade) onde a instituição está sediada (Importado via XML).
        /// </summary>
        [Required(ErrorMessage = "A {0} é de preenchimento obrigatório.")]
        [Display(Name = "Localidade")]
        public string Localidade { get; set; } = string.Empty;

        /// <summary>
        /// Código postal da instituição parceira (Importado via XML).
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "Código Postal")]
        public string CodigoPostal { get; set; } = string.Empty;

        /// <summary>
        /// País onde a instituição parceira está sediada (Importado via XML).
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "País")]
        public string Pais { get; set; } = string.Empty;

        /// <summary>
        /// Endereço web (URL) oficial da universidade parceira (Importado via XML).
        /// </summary>
        [Url(ErrorMessage = "O formato do link inserido é inválido. Tente usar http:// ou https://")]
        [Display(Name = "Website da Universidade")]
        public string? Url { get; set; }

        /// <summary>
        /// Tipo de Acordo Bilateral estabelecido com o IPT (Ex: SMS para Estudos, SMP para Estágios).
        /// Importado via XML para filtrar as universidades disponíveis consoante o objetivo do aluno.
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "Tipo de Acordo (Ex: SMS, SMP)")]
        public string TipoAcordo { get; set; } = string.Empty;

        /// <summary>
        /// Propriedade de navegação: Liga a universidade às várias candidaturas onde foi escolhida.
        /// </summary>
        public virtual ICollection<CandidaturaUniversidade> CandidaturasUniversidades { get; set; } = [];
    }
}