using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace appErasmus.Models {
    /// <summary>
    /// Estados possíveis da candidatura da universidade individualmente.
    /// Permite gerir respostas independentes de cada instituição de destino.
    /// </summary>
    public enum EnumEstadoCandidaturaUniversidade {
        [Display(Name = "Aceite")]
        Aceite = 1,

        [Display(Name = "Negada")]
        Negada = 2,

        [Display(Name = "Em Processamento")]
        EmProcessamento = 3,

        [Display(Name = "Finalizado")]
        Finalizado = 4,
    }

    /// <summary>
    /// Tabela intermédia que resolve a relação de Muitos-para-Muitos entre Candidaturas e Universidades.
    /// Utiliza uma chave primária composta para garantir que o aluno não escolhe a mesma universidade duas vezes.
    /// </summary>
    [PrimaryKey(nameof(CandidaturaFK), nameof(UniversidadeFK))]
    public class CandidaturaUniversidade {

        /// <summary>
        /// Nível de prioridade dado pelo aluno a esta instituição (ex: 1 = Primeira Escolha, 2 = Segunda).
        /// </summary>
        [Display(Name = "Ordem de Preferência")]
        public int Preferencia { get; set; }

        /// <summary>
        /// Chave estrangeira que liga este registo à candidatura principal (Protegido por GUID).
        /// </summary>
        [Display(Name = "Identificador da Candidatura")]
        public Guid CandidaturaFK { get; set; }

        /// <summary>
        /// Propriedade de navegação obrigatória para aceder aos dados da candidatura em memória.
        /// </summary>
        [ForeignKey(nameof(CandidaturaFK))]
        public required Candidatura Candidatura { get; set; }

        /// <summary>
        /// Chave estrangeira que liga este registo ao código Erasmus da universidade parceira.
        /// </summary>
        [Required(ErrorMessage = "A Universidade é de preenchimento obrigatório.")]
        [Display(Name = "Universidade")]
        public string UniversidadeFK { get; set; } = string.Empty;

        /// <summary>
        /// Propriedade de navegação obrigatória para aceder aos dados da universidade.
        /// </summary>
        [ForeignKey(nameof(UniversidadeFK))]
        public required Universidade Universidade { get; set; }

        /// <summary>
        /// Se o aluno escolher a opção "Outra" este campo guarda o nome livre da universidade.
        /// </summary>
        [Display(Name = "Universidade (Outra)")]
        [StringLength(250)]
        public string? OutraUniversidade { get; set; }

        /// <summary>
        /// Indica a decisão ou o andamento processual exclusivo desta universidade perante a candidatura.
        /// </summary>
        [Display(Name = "Estado na Universidade")]
        public EnumEstadoCandidaturaUniversidade EstadoCandidatura { get; set; }
    }
}