package cz.stochel.ormconvertor.javatests.tool;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * The running instance of the tool, as this suite reaches it: over HTTP, as any other
 * client would (decision 078). The translation path emits Java from a .NET process and
 * this suite lives in a JVM, so the process boundary is crossed by the product surface
 * the tool already has - the REST contract of decision 043 - and not by a format invented
 * for the tests.
 *
 * <p>The address is configuration and its absence is a failure, the same rule the JDBC URL
 * follows: the suite runs only where someone started an instance for it, so skipping would
 * have nothing to say. On that instance the suite waits by itself, because the image the
 * compose service is built from carries no {@code curl} for a health check and the CI job
 * has no compose at all.
 */
public final class ToolApi {

    /** Base address including the deployment path {@code /orm}. */
    public static final String API_URL_VARIABLE = "ORMCONVERTOR_API_URL";

    private static final Duration READY_TIMEOUT = Duration.ofMinutes(3);
    private static final Duration READY_POLL = Duration.ofSeconds(2);
    private static final Duration REQUEST_TIMEOUT = Duration.ofMinutes(2);

    private static final ObjectMapper MAPPER = new ObjectMapper();

    private static final HttpClient CLIENT = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(10))
            .build();

    private ToolApi() {
    }

    public static String baseUrl() {
        String url = System.getenv(API_URL_VARIABLE);
        if (url == null || url.isBlank()) {
            throw new IllegalStateException(
                    "No instance of the tool configured. Set " + API_URL_VARIABLE + " to its base address "
                    + "including the deployment path, such as \"http://localhost:5072/orm\", or run the suite "
                    + "in its container with \"docker compose --profile test run --rm java_tests\", which "
                    + "starts the instance as the service test_app.");
        }
        return url.endsWith("/") ? url.substring(0, url.length() - 1) : url;
    }

    /**
     * Waits until the instance answers. {@code /required-content} is the cheapest endpoint
     * that proves the whole pipeline is up - it is a reading endpoint with no body and no
     * database behind it - and 200 from it is the condition the compose ordering cannot
     * express on its own.
     */
    public static void awaitReady() {
        String url = baseUrl() + "/required-content";
        long deadline = System.nanoTime() + READY_TIMEOUT.toNanos();
        String lastFailure = "no attempt was made";

        while (System.nanoTime() < deadline) {
            try {
                HttpResponse<String> response = CLIENT.send(
                        HttpRequest.newBuilder(URI.create(url)).GET().timeout(REQUEST_TIMEOUT).build(),
                        HttpResponse.BodyHandlers.ofString());

                if (response.statusCode() == 200) {
                    return;
                }
                lastFailure = "it answered " + response.statusCode();
            } catch (IOException e) {
                lastFailure = e.getClass().getSimpleName() + ": " + e.getMessage();
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new IllegalStateException("Interrupted while waiting for " + url, e);
            }

            try {
                Thread.sleep(READY_POLL);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new IllegalStateException("Interrupted while waiting for " + url, e);
            }
        }

        throw new IllegalStateException("The instance at " + url + " did not answer within "
                + READY_TIMEOUT.toSeconds() + " s; last attempt: " + lastFailure);
    }

    /**
     * One translation: the units of the request in the order given, the answer as the
     * contract describes it. A non-200 comes back as the status code and the body rather
     * than as an exception, so the scenario can assert the code and print the reason.
     */
    public static ToolResponse convert(int sourceOrm, int targetOrm, List<InputUnit> units) {
        Map<String, Object> request = new LinkedHashMap<>();
        request.put("sourceOrm", sourceOrm);
        request.put("targetOrm", targetOrm);
        request.put("sources", units);

        try {
            HttpResponse<String> response = CLIENT.send(
                    HttpRequest.newBuilder(URI.create(baseUrl() + "/convert"))
                            .header("Content-Type", "application/json")
                            .timeout(REQUEST_TIMEOUT)
                            .POST(HttpRequest.BodyPublishers.ofString(MAPPER.writeValueAsString(request)))
                            .build(),
                    HttpResponse.BodyHandlers.ofString());

            ConversionResponse conversion = response.statusCode() == 200
                    ? MAPPER.readValue(response.body(), ConversionResponse.class)
                    : null;

            return new ToolResponse(response.statusCode(), response.body(), conversion);
        } catch (IOException e) {
            throw new IllegalStateException("The conversion request to " + baseUrl() + "/convert failed.", e);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("Interrupted during the conversion request.", e);
        }
    }
}
