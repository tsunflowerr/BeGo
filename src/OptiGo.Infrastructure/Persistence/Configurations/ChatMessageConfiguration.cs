using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Persistence.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(m => m.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(m => m.MemberId)
            .HasColumnName("member_id")
            .IsRequired();

        builder.Property(m => m.SenderName)
            .HasColumnName("sender_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.Text)
            .HasColumnName("text")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(m => new { m.SessionId, m.CreatedAt })
            .HasDatabaseName("idx_chat_messages_session_created_at");

        builder.HasOne(m => m.Session)
            .WithMany()
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Member)
            .WithMany()
            .HasForeignKey(m => m.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
