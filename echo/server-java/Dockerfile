# Build stage
FROM maven:3.9-eclipse-temurin-25 AS builder

WORKDIR /app

COPY pom.xml .
RUN mvn dependency:go-offline

COPY . .
RUN mvn clean package -DskipTests

# Run stage
FROM eclipse-temurin:25-jre

WORKDIR /app

COPY --from=builder /app/target/server-java-1.0.0.jar app.jar

EXPOSE 3010

ENTRYPOINT ["java", "-jar", "app.jar"]

