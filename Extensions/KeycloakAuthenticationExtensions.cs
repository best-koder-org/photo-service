using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;

namespace PhotoService.Extensions
{
    public static class KeycloakAuthenticationExtensions
    {
        public static IServiceCollection AddKeycloakAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            var keycloakSection = configuration.GetSection("Authentication:Keycloak");
            if (!keycloakSection.Exists())
            {
                throw new InvalidOperationException("Authentication:Keycloak configuration section is required.");
            }

            var authority = keycloakSection["Authority"];
            if (string.IsNullOrWhiteSpace(authority))
            {
                throw new InvalidOperationException("Authentication:Keycloak:Authority configuration is required.");
            }

            var audiences = BuildAudienceList(keycloakSection);
            var requireHttpsMetadata = keycloakSection.GetValue("RequireHttpsMetadata", true);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.RequireHttpsMetadata = requireHttpsMetadata;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = audiences.Count > 0,
                    ValidAudiences = audiences,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    NameClaimType = "preferred_username",
                    RoleClaimType = "roles"
                };
            });

            return services;
        }

        private static List<string> BuildAudienceList(IConfigurationSection keycloakSection)
        {
            var audiences = new List<string>();

            var clientId = keycloakSection["ClientId"];
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                audiences.Add(clientId);
            }

            var audience = keycloakSection["Audience"];
            if (!string.IsNullOrWhiteSpace(audience) && !audiences.Contains(audience))
            {
                audiences.Add(audience);
            }

            var configuredAudiences = keycloakSection.GetSection("Audiences").Get<string[]>();
            if (configuredAudiences != null)
            {
                foreach (var configuredAudience in configuredAudiences)
                {
                    if (!string.IsNullOrWhiteSpace(configuredAudience) && !audiences.Contains(configuredAudience))
                    {
                        audiences.Add(configuredAudience);
                    }
                }
            }

            return audiences;
        }
    }
}
