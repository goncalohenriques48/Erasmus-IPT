using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace appErasmus.Models {
    /// <summary>
    /// Classe que representa o registo de um documento ou ficheiro físico submetido no sistema.
    /// Centraliza o mapeamento de ficheiros locais organizados por candidaturas e categorias de validação.
    /// </summary>
    public class Documento {
        /// <summary>
        /// Identificador único do documento. 
        /// Utiliza um GUID por motivos de segurança, impedindo a exposição sequencial de ficheiros no servidor.
        /// </summary>
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// O nome original do ficheiro no momento do upload efetuado pelo utilizador (Ex: "comprovativo_iban.pdf").
        /// </summary>
        [Required(ErrorMessage = "O nome original do ficheiro é obrigatório.")]
        [StringLength(255, ErrorMessage = "O nome do ficheiro não pode exceder 255 caracteres.")]
        [Display(Name = "Nome Original do Ficheiro")]
        public string NomeOriginal { get; set; } = string.Empty;

        /// <summary>
        /// O nome físico ou caminho único de armazenamento gerado pelo sistema no disco do servidor.
        /// Exemplo de máscara: "CandidaturaID_GUID.pdf" para evitar a sobreposição de ficheiros homónimos.
        /// </summary>
        [Required]
        public string CaminhoFicheiroSistema { get; set; } = string.Empty;

        /// <summary>
        /// Data e hora em que o upload do documento foi realizado.
        /// </summary>
        [Display(Name = "Data de Criação")]
        public DateTime DataCriacao { get; set; } = DateTime.Now;

        /// <summary>
        /// Data e hora em que o documento foi substituído ou alterado (Nulo se nunca tiver sido modificado).
        /// Nota: Mantém-se o tipo anulável por ser um tipo de valor estrutural para a base de dados SQL.
        /// </summary>
        [Display(Name = "Última Atualização")]
        public DateTime? DataAtualizacao { get; set; }

        // --- CATEGORIA DOCUMENTAL ---

        /// <summary>
        /// Chave estrangeira que associa o ficheiro a uma tipologia documental (Ex: Learning_Agreement).
        /// </summary>
        [Required(ErrorMessage = "A categoria do documento é obrigatória.")]
        [Display(Name = "Categoria")]
        public string CategoriaFK { get; set; } = string.Empty;

        /// <summary>
        /// Propriedade de navegação para obter as regras e restrições da Categoria do Documento.
        /// </summary>
        [ForeignKey(nameof(CategoriaFK))]
        public virtual CategoriaDocumento Categoria { get; set; } = null!;

        // --- CANDIDATURA ASSOCIADA ---

        /// <summary>
        /// Chave estrangeira que vincula o documento à candidatura correspondente (Protegido por GUID).
        /// </summary>
        [Required(ErrorMessage = "A candidatura associada é obrigatória.")]
        [Display(Name = "Candidatura Associada")]
        public Guid CandidaturaFK { get; set; }

        /// <summary>
        /// Propriedade de navegação para aceder aos metadados da candidatura à qual o ficheiro pertence.
        /// </summary>
        [ForeignKey(nameof(CandidaturaFK))]
        public virtual Candidatura Candidatura { get; set; } = null!;

        // --- AUDITORIA: CRIADOR ---
        
        /// <summary>
        /// Chave estrangeira que identifica o utilizador que efetuou o upload original do ficheiro.
        /// </summary>
        [Required]
        [Display(Name = "Submetido Por")]
        public string CreatedByFK { get; set; } = string.Empty;

        /// <summary>
        /// Propriedade de navegação para obter o perfil do utilizador criador do documento.
        /// </summary>
        [ForeignKey(nameof(CreatedByFK))]
        public virtual Utilizador CreatedBy { get; set; } = null!;

        // --- Atualizador (Auditoria: Quem fez a última alteração) ---

        /// <summary>
        /// Chave estrangeira que identifica o último utilizador que modificou ou substituiu o ficheiro.
        /// Inicializado em vazio por não ser obrigatório na criação do registo.
        /// </summary>
        [Display(Name = "Atualizado Por")]
        public string UpdatedByFK { get; set; } = string.Empty;

        /// <summary>
        /// Propriedade de navegação para o perfil do utilizador responsável pela última edição.
        /// </summary>
        [ForeignKey(nameof(UpdatedByFK))]
        public virtual Utilizador UpdatedBy { get; set; } = null!;

        /// <summary>
        /// Descrição opcional do documento, permitindo ao utilizador fornecer contexto adicional sobre o ficheiro submetido.
        /// </summary>
        [Display(Name = "Descrição do Documento")]
        [StringLength(150, ErrorMessage = "A descrição não pode exceder os {1} caracteres.")]
        public string DescricaoDocumento { get; set; } = string.Empty;
    }
}