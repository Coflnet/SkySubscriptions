﻿// <auto-generated />
using System;
using Coflnet.Sky.Subscriptions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace SkySubscriptions.Migrations
{
    [DbContext(typeof(SubsDbContext))]
    [Migration("20220126224928_filter")]
    partial class filter
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 64);

            modelBuilder.Entity("Coflnet.Sky.Subscriptions.Models.Device", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    b.Property<string>("ConnectionId")
                        .HasMaxLength(32)
                        .HasColumnType("varchar(32)");

                    b.Property<string>("Name")
                        .HasMaxLength(40)
                        .HasColumnType("varchar(40)");

                    b.Property<string>("Token")
                        .HasColumnType("longtext");

                    b.Property<int>("UserId")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("UserId", "Name")
                        .IsUnique();

                    b.ToTable("Device");
                });

            modelBuilder.Entity("Coflnet.Sky.Subscriptions.Models.Subscription", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    b.Property<string>("Filter")
                        .HasMaxLength(200)
                        .HasColumnType("varchar(200)");

                    b.Property<DateTime>("GeneratedAt")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("datetime(6)");

                    b.Property<DateTime>("NotTriggerAgainBefore")
                        .HasColumnType("datetime(6)");

                    b.Property<long>("Price")
                        .HasColumnType("bigint");

                    b.Property<string>("TopicId")
                        .HasMaxLength(45)
                        .HasColumnType("varchar(45)");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.Property<int>("UserId")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("Subscriptions");
                });

            modelBuilder.Entity("Coflnet.Sky.Subscriptions.Models.User", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    b.Property<string>("ExternalId")
                        .HasColumnType("varchar(255)");

                    b.HasKey("Id");

                    b.HasIndex("ExternalId")
                        .IsUnique();

                    b.ToTable("Users");
                });

            modelBuilder.Entity("Coflnet.Sky.Subscriptions.Models.Device", b =>
                {
                    b.HasOne("Coflnet.Sky.Subscriptions.Models.User", null)
                        .WithMany("Devices")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Coflnet.Sky.Subscriptions.Models.Subscription", b =>
                {
                    b.HasOne("Coflnet.Sky.Subscriptions.Models.User", "User")
                        .WithMany("Subscriptions")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });

            modelBuilder.Entity("Coflnet.Sky.Subscriptions.Models.User", b =>
                {
                    b.Navigation("Devices");

                    b.Navigation("Subscriptions");
                });
#pragma warning restore 612, 618
        }
    }
}
